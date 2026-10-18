using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Sgm.Core.Logging;
using Sgm.Core.Text;

namespace Sgm.Core.Artwork;

public sealed class SteamGridDbException : Exception
{
    public SteamGridDbException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// SteamGridDB API v2 client: search, per-kind assets and streaming downloads.
/// </summary>
public sealed class SteamGridDbClient
{
    private const string Base = "https://www.steamgriddb.com/api/v2";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly (string Kind, string Template)[] AssetFiles =
    {
        ("grids", "{0}p.jpg"),
        ("heroes", "{0}_hero.jpg"),
        ("logos", "{0}_logo.png"),
        ("icons", "{0}.ico"),
    };

    private readonly HttpClient _client;

    public string ApiKey { get; }

    public SteamGridDbClient(string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("SteamGridDB API key is required", nameof(apiKey));
        ApiKey = apiKey.Trim();
        _client = http ?? new HttpClient { Timeout = Timeout };
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
    }

    private List<JsonElement> Get(string path, IReadOnlyDictionary<string, string>? query = null)
    {
        var url = Base + path;
        if (query is { Count: > 0 })
        {
            var parts = query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
            url += "?" + string.Join("&", parts);
        }

        HttpResponseMessage response;
        try
        {
            response = _client.GetAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new SteamGridDbException($"Network error calling {path}: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new SteamGridDbException("SteamGridDB rejected the API key (401 Unauthorized)");
            if ((int)response.StatusCode == 429)
                throw new SteamGridDbException("SteamGridDB rate limit hit (429); retry later");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }
            catch (JsonException ex)
            {
                throw new SteamGridDbException($"Non-JSON response from {path}: HTTP {(int)response.StatusCode}", ex);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                if (!response.IsSuccessStatusCode || !success)
                {
                    var errors = root.TryGetProperty("errors", out var e) ? e.ToString() : $"HTTP {(int)response.StatusCode}";
                    throw new SteamGridDbException($"SteamGridDB {path} failed: {errors}");
                }

                if (!root.TryGetProperty("data", out var data))
                    return new List<JsonElement>();
                if (data.ValueKind == JsonValueKind.Array)
                    return data.EnumerateArray().Select(x => x.Clone()).ToList();
                return new List<JsonElement> { data.Clone() };
            }
        }
    }

    public List<JsonElement> SearchGame(string name)
        => Get($"/search/autocomplete/{Uri.EscapeDataString(name)}");

    /// <summary>Cheap key check: any authenticated call works; 401/429 raise.</summary>
    public bool Validate()
    {
        Get("/search/autocomplete/steam");
        return true;
    }

    /// <summary>Best candidate: exact (case-insensitive) name match wins; fuzzy only when allowed.</summary>
    public JsonElement? PickGame(string name, bool exactOnly = false)
    {
        var candidates = SearchGame(name);
        if (candidates.Count == 0)
            return null;

        var want = TitleNormalizer.Normalize(name);
        foreach (var c in candidates)
        {
            var candidateName = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (want.Length > 0 && TitleNormalizer.Normalize(candidateName) == want)
                return c;
        }
        return exactOnly ? null : candidates[0];
    }

    public List<JsonElement> Assets(int sgdbId, string kind)
    {
        var query = new Dictionary<string, string> { ["types"] = "static", ["limit"] = "10" };
        var dim = kind switch
        {
            "grids" => "600x900",
            "heroes" => "1920x620",
            _ => null,
        };
        if (dim != null)
            query["dimensions"] = dim;
        return Get($"/games/id/{sgdbId}/{kind}", query);
    }

    public bool Download(string url, string dest)
    {
        try
        {
            using var response = _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".tmp";
            using (var input = response.Content.ReadAsStream())
            using (var output = File.Create(tmp))
            {
                input.CopyTo(output);
            }
            File.Move(tmp, dest, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Artwork download failed {url}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>SteamGridDB per-game artwork download (keyed, best custom art).</summary>
public static class SteamGridDbArtwork
{
    private static readonly (string Kind, string Template)[] AssetFiles =
    {
        ("grids", "{0}p.jpg"),
        ("heroes", "{0}_hero.jpg"),
        ("logos", "{0}_logo.png"),
        ("icons", "{0}.ico"),
    };

    /// <summary>Download best artwork for one game. Never throws; returns saved files.</summary>
    public static Dictionary<string, string> Fetch(SteamGridDbClient client, string gameName, uint appid,
        string gridDir, int? sgdbId = null, bool exactOnly = true)
    {
        var saved = new Dictionary<string, string>();
        try
        {
            var gid = sgdbId;
            if (gid == null)
            {
                var picked = client.PickGame(gameName, exactOnly);
                if (picked == null)
                {
                    AppLog.Info($"SteamGridDB: no exact match for '{gameName}' — art skipped (pick manually)");
                    return saved;
                }
                gid = picked.Value.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : null;
                if (gid == null)
                    return saved;
            }

            foreach (var (kind, template) in AssetFiles)
            {
                List<JsonElement> items;
                try
                {
                    items = client.Assets(gid.Value, kind);
                }
                catch (SteamGridDbException ex)
                {
                    AppLog.Warn($"SteamGridDB {kind} for '{gameName}' failed: {ex.Message}");
                    continue;
                }
                if (items.Count == 0)
                    continue;
                if (!items[0].TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
                    continue;
                var url = urlEl.GetString();
                if (string.IsNullOrEmpty(url))
                    continue;
                var dest = Path.Combine(gridDir, string.Format(template, appid));
                if (client.Download(url, dest))
                    saved[kind] = dest;
            }
        }
        catch (SteamGridDbException ex)
        {
            AppLog.Warn($"Artwork lookup for '{gameName}' failed: {ex.Message}");
        }
        return saved;
    }
}
