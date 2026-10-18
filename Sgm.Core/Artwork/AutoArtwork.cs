using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sgm.Core.Logging;
using Sgm.Core.Text;

namespace Sgm.Core.Artwork;

/// <summary>Result of a keyless artwork fetch: saved kind→path plus the provider that won.</summary>
public sealed record AutoArtResult(Dictionary<string, string> Saved, string? Provider);

/// <summary>A keyless artwork source (Epic / Steam / GOG catalogs).</summary>
public interface IArtProvider
{
    string Name { get; }

    /// <summary>Ranked image URLs per kind, or null when there is no exact title match.</summary>
    Dictionary<string, List<string>>? Resolve(string title);
}

/// <summary>
/// Fully automatic (keyless) artwork with strict type mapping:
/// source-native provider first, Steam store fallback second.
/// </summary>
public static partial class AutoArtwork
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private static readonly HashSet<string> VideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "herocarouselvideo", "videotrailer", "video",
    };

    private static readonly string[] EpicGridTypes = { "DieselGameBoxTall", "OfferImageTall", "DieselStoreFrontTall" };
    private static readonly string[] EpicHeroTypes = { "DieselStoreFrontWide", "OfferImageWide", "DieselGameBoxWide" };
    private static readonly string[] EpicLogoTypes = { "DieselGameBoxLogo", "ProductLogo", "DieselStoreFrontLogo" };
    private static readonly string[] EpicIconTypes = { "Thumbnail" };
    private static readonly string[] EpicExtraTypes = { "featuredMedia" };

    [GeneratedRegex("(\\d{3,5})\\s*x\\s*(\\d{3,5})")]
    private static partial Regex DimRegex();

    private static long UrlArea(string url)
    {
        var m = DimRegex().Match(url);
        return m.Success ? long.Parse(m.Groups[1].Value) * long.Parse(m.Groups[2].Value) : 0;
    }

    private static string? VideoCover(string url)
    {
        try
        {
            var query = url.Split('?', 2);
            if (query.Length < 2)
                return null;
            foreach (var pair in query[1].Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "cover")
                {
                    var cover = Uri.UnescapeDataString(kv[1]);
                    if (cover.StartsWith("http", StringComparison.Ordinal))
                        return cover;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    /// <summary>Candidate image URLs, best first (exact types, then area / aspect-ranked extras).</summary>
    public static List<string> RankedUrls(List<Dictionary<string, string>> keyImages, IReadOnlyCollection<string> wanted,
        double targetAspect, bool extras = true)
    {
        var primary = new List<(long Area, string Url)>();
        var spare = new List<(double Diff, long Area, string Url)>();

        foreach (var ki in keyImages)
        {
            var type = ki.GetValueOrDefault("type", "");
            var url = ki.GetValueOrDefault("url", "");
            if (url.Length == 0)
                continue;

            if (VideoTypes.Contains(type))
            {
                var cover = VideoCover(url);
                if (cover != null && extras)
                    spare.Add((Math.Abs((16.0 / 9.0) / targetAspect - 1), -UrlArea(cover), cover));
                continue;
            }

            if (wanted.Contains(type))
            {
                primary.Add((-UrlArea(url), url));
            }
            else if (extras && EpicExtraTypes.Contains(type))
            {
                var m = DimRegex().Match(url);
                if (m.Success)
                {
                    var aspect = double.Parse(m.Groups[1].Value) / double.Parse(m.Groups[2].Value);
                    spare.Add((Math.Abs(aspect / targetAspect - 1), -long.Parse(m.Groups[1].Value) * long.Parse(m.Groups[2].Value), url));
                }
                else
                {
                    spare.Add((1.0, 0, url));
                }
            }
        }

        primary.Sort((a, b) => a.Area.CompareTo(b.Area));
        spare.Sort((a, b) =>
        {
            var c = a.Diff.CompareTo(b.Diff);
            return c != 0 ? c : a.Area.CompareTo(b.Area);
        });

        var ordered = primary.Select(p => p.Url).Concat(spare.Select(s => s.Url));
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var u in ordered)
            if (seen.Add(u))
                result.Add(u);
        return result;
    }

    public static Dictionary<string, string> Materialize(Dictionary<string, List<string>> urls, uint appid, string gridDir)
    {
        var saved = new Dictionary<string, string>();

        Bitmap? FirstImage(IEnumerable<string> candidates)
        {
            foreach (var url in candidates)
            {
                var img = ImageService.Download(url);
                if (img != null)
                    return img;
            }
            return null;
        }

        if (urls.TryGetValue("grids", out var grids) && grids.Count > 0)
        {
            using var img = FirstImage(grids);
            if (img != null)
            {
                using var resized = ImageService.CoverResize(img, ImageService.GridSize);
                var dest = Path.Combine(gridDir, $"{appid}p.jpg");
                if (ImageService.SaveJpg(resized, dest))
                    saved["grids"] = dest;
            }
        }
        else if (urls.TryGetValue("squares", out var squares) && squares.Count > 0)
        {
            var cover = SquareCoverImage(urls, FirstImage);
            if (cover != null)
            {
                using (cover)
                {
                    var dest = Path.Combine(gridDir, $"{appid}p.jpg");
                    if (ImageService.SaveJpg(cover, dest))
                        saved["grids"] = dest;
                }
            }
        }

        if (urls.TryGetValue("heroes", out var heroes) && heroes.Count > 0)
        {
            using var img = FirstImage(heroes);
            if (img != null)
            {
                using var resized = ImageService.CoverResize(img, ImageService.HeroSize);
                var dest = Path.Combine(gridDir, $"{appid}_hero.jpg");
                if (ImageService.SaveJpg(resized, dest))
                    saved["heroes"] = dest;
            }
        }

        if (urls.TryGetValue("logos", out var logos) && logos.Count > 0)
        {
            using var img = FirstImage(logos);
            if (img != null)
            {
                using var trimmed = ImageService.TrimTransparent(img);
                var dest = Path.Combine(gridDir, $"{appid}_logo.png");
                if (ImageService.SavePng(trimmed, dest))
                    saved["logos"] = dest;
            }
        }

        var iconCandidates = urls.GetValueOrDefault("icons")
                             ?? urls.GetValueOrDefault("grids")
                             ?? urls.GetValueOrDefault("squares")
                             ?? urls.GetValueOrDefault("logos");
        if (iconCandidates is { Count: > 0 })
        {
            using var img = FirstImage(iconCandidates);
            if (img != null)
            {
                var dest = Path.Combine(gridDir, $"{appid}.ico");
                if (ImageService.SaveIco(img, dest))
                    saved["icons"] = dest;
            }
        }

        return saved;
    }

    private static Bitmap? SquareCoverImage(Dictionary<string, List<string>> urls, Func<IEnumerable<string>, Bitmap?> fetch)
    {
        var square = fetch(urls["squares"]);
        if (square == null)
            return null;
        using (square)
        {
            Bitmap? backdrop = null;
            if (urls.TryGetValue("heroes", out var heroes) && heroes.Count > 0)
                backdrop = fetch(heroes);
            using (backdrop)
            {
                return ImageService.ComposeSquareCover(square, backdrop, ImageService.GridSize);
            }
        }
    }

    /// <summary>Cover image from a game's own store art, for the library thumbnail.</summary>
    public static Bitmap? LocalThumbImage(IReadOnlyDictionary<string, string> artUrls)
    {
        if (artUrls.TryGetValue("grids", out var grids) && grids.Length > 0)
            return ImageService.Download(grids);
        if (artUrls.TryGetValue("squares", out var squares) && squares.Length > 0)
        {
            var local = new Dictionary<string, List<string>> { ["squares"] = new() { squares } };
            if (artUrls.TryGetValue("heroes", out var heroes) && heroes.Length > 0)
                local["heroes"] = new List<string> { heroes };
            return SquareCoverImage(local, candidates => { foreach (var c in candidates) { var i = ImageService.Download(c); if (i != null) return i; } return null; });
        }
        return null;
    }

    private static List<IArtProvider> ProvidersFor(string source)
    {
        var providers = new List<IArtProvider>();
        if (source == Models.GameSource.Epic)
            providers.Add(new EpicProvider());
        else if (source == Models.GameSource.Gog)
            providers.Add(new GogProvider());
        providers.Add(new SteamProvider());
        return providers;
    }

    /// <summary>Best-effort keyless artwork. Only fills kinds in <paramref name="missing"/>.</summary>
    public static AutoArtResult Fetch(string gameName, string source, uint appid, string gridDir,
        IReadOnlySet<string>? missing = null, IReadOnlyDictionary<string, string>? localUrls = null)
    {
        var want = missing ?? new HashSet<string> { "grids", "heroes", "logos", "icons" };
        if (want.Count == 0)
            return new AutoArtResult(new Dictionary<string, string>(), null);

        if (localUrls is { Count: > 0 })
        {
            var local = new Dictionary<string, List<string>>();
            foreach (var (k, u) in localUrls)
            {
                if (u.Length == 0)
                    continue;
                if (want.Contains(k))
                    local[k] = new List<string> { u };
                else if (k == "squares" && want.Contains("grids"))
                    local[k] = new List<string> { u };
            }

            if (local.Count > 0)
            {
                var saved = Materialize(local, appid, gridDir);
                if (saved.Count > 0)
                {
                    AppLog.Info($"Auto-art: '{gameName}' <- local store cache ({string.Join(", ", saved.Keys.OrderBy(x => x))})");
                    var remaining = new HashSet<string>(want.Except(saved.Keys));
                    if (remaining.Count == 0)
                        return new AutoArtResult(saved, "local");
                    var more = Fetch(gameName, source, appid, gridDir, remaining, null);
                    foreach (var (k, v) in more.Saved)
                        saved[k] = v;
                    return new AutoArtResult(saved, more.Provider ?? "local");
                }
            }
        }

        foreach (var provider in ProvidersFor(source))
        {
            Dictionary<string, List<string>>? urls;
            try
            {
                urls = provider.Resolve(gameName);
            }
            catch (Exception ex)
            {
                AppLog.Info($"{provider.Name} provider error for '{gameName}': {ex.Message}");
                continue;
            }
            if (urls == null)
                continue;

            var filtered = urls.Where(kv => want.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (filtered.Count == 0)
                continue;

            var saved = Materialize(filtered, appid, gridDir);
            if (saved.Count > 0)
            {
                AppLog.Info($"Auto-art: '{gameName}' <- {provider.Name} ({string.Join(", ", saved.Keys.OrderBy(x => x))})");
                return new AutoArtResult(saved, provider.Name);
            }
            AppLog.Info($"Auto-art: '{gameName}' matched on {provider.Name} but downloads failed");
        }

        AppLog.Info($"Auto-art: no exact match for '{gameName}'");
        return new AutoArtResult(new Dictionary<string, string>(), null);
    }

    /// <summary>One-line description of the automatic match (for the GUI dialog).</summary>
    public static string MatchLabel(string gameName, string source)
    {
        foreach (var provider in ProvidersFor(source))
        {
            try
            {
                var urls = provider.Resolve(gameName);
                if (urls != null)
                    return $"Auto ({provider.Name}): match found";
            }
            catch
            {
                // try next provider
            }
        }
        return "Auto: no exact match found";
    }

    /// <summary>Best small portrait-ish image URL for library card thumbnails.</summary>
    public static string? ThumbnailUrl(string gameName, string source)
    {
        foreach (var provider in ProvidersFor(source))
        {
            Dictionary<string, List<string>>? urls;
            try
            {
                urls = provider.Resolve(gameName);
            }
            catch
            {
                continue;
            }
            if (urls == null)
                continue;
            foreach (var kind in new[] { "icons", "grids", "heroes", "logos" })
                if (urls.TryGetValue(kind, out var list) && list.Count > 0)
                    return list[0];
        }
        return null;
    }

    // ---- Providers -----------------------------------------------------------------

    private sealed class EpicProvider : IArtProvider
    {
        public string Name => "epic";

        public Dictionary<string, List<string>>? Resolve(string title)
        {
            var elements = Query(title);
            if (elements == null)
                return null;

            var want = TitleNormalizer.Normalize(title);
            var exact = elements.Where(e => want.Length > 0 && TitleNormalizer.Normalize(Get(e, "title")) == want).ToList();
            if (exact.Count == 0)
                return null;

            var best = exact.OrderByDescending(EpicScore).First();
            var keyImages = KeyImages(best);
            var result = new Dictionary<string, List<string>>();
            Add(result, "grids", RankedUrls(keyImages, EpicGridTypes, (double)ImageService.GridSize.Width / ImageService.GridSize.Height));
            Add(result, "heroes", RankedUrls(keyImages, EpicHeroTypes, (double)ImageService.HeroSize.Width / ImageService.HeroSize.Height));
            Add(result, "logos", RankedUrls(keyImages, EpicLogoTypes, 0, extras: false));
            Add(result, "icons", RankedUrls(keyImages, EpicIconTypes, 1, extras: false));
            return result.Count > 0 ? result : null;
        }

        private static void Add(Dictionary<string, List<string>> dict, string key, List<string> urls)
        {
            if (urls.Count > 0)
                dict[key] = urls;
        }

        private static (int Kinds, long Area) EpicScore(JsonElement element)
        {
            var keyImages = KeyImages(element);
            var types = keyImages.Select(k => k.GetValueOrDefault("type", "")).ToHashSet(StringComparer.Ordinal);
            var kinds = 0;
            foreach (var pool in new[] { EpicGridTypes, EpicHeroTypes, EpicLogoTypes, EpicIconTypes })
                if (pool.Any(types.Contains))
                    kinds++;
            if (keyImages.Any(k => VideoTypes.Contains(k.GetValueOrDefault("type", "")) || EpicExtraTypes.Contains(k.GetValueOrDefault("type", ""))))
                kinds++;
            var area = keyImages.Sum(k => UrlArea(k.GetValueOrDefault("url", "")));
            return (kinds, area);
        }

        private static string Get(JsonElement el, string key)
            => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static List<Dictionary<string, string>> KeyImages(JsonElement element)
        {
            var result = new List<Dictionary<string, string>>();
            if (!element.TryGetProperty("keyImages", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var ki in arr.EnumerateArray())
            {
                if (ki.ValueKind != JsonValueKind.Object)
                    continue;
                result.Add(new Dictionary<string, string>
                {
                    ["type"] = Get(ki, "type"),
                    ["url"] = Get(ki, "url"),
                });
            }
            return result;
        }

        private static List<JsonElement>? Query(string title)
        {
            const string query =
                "query searchStoreQuery($keywords: String!, $count: Int!) { " +
                "Catalog { searchStore(keywords: $keywords, count: $count) { elements { title id keyImages { type url } } } } }";
            var payload = JsonSerializer.Serialize(new
            {
                query,
                variables = new { keywords = title, count = 10 },
            });

            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = Client.PostAsync("https://graphql.epicgames.com/graphql", content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var elements = doc.RootElement
                    .GetProperty("data").GetProperty("Catalog").GetProperty("searchStore").GetProperty("elements");
                if (elements.ValueKind != JsonValueKind.Array)
                    return null;
                return elements.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Info($"Epic search failed for '{title}': {ex.Message}");
                return null;
            }
        }
    }

    private sealed class SteamProvider : IArtProvider
    {
        public string Name => "steam";

        public Dictionary<string, List<string>>? Resolve(string title)
        {
            try
            {
                var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&l=english&cc=US";
                var json = Client.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return null;

                var want = TitleNormalizer.Normalize(title);
                foreach (var item in items.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (TitleNormalizer.Normalize(name) != want)
                        continue;
                    if (!item.TryGetProperty("id", out var idEl))
                        continue;
                    var appid = idEl.GetInt64();
                    var baseUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appid}";
                    return new Dictionary<string, List<string>>
                    {
                        ["grids"] = new() { $"{baseUrl}/library_600x900.jpg" },
                        ["heroes"] = new() { $"{baseUrl}/library_hero.jpg" },
                        ["logos"] = new() { $"{baseUrl}/logo.png" },
                    };
                }
            }
            catch (Exception ex)
            {
                AppLog.Info($"Steam search failed for '{title}': {ex.Message}");
            }
            return null;
        }
    }

    private sealed class GogProvider : IArtProvider
    {
        public string Name => "gog";

        public Dictionary<string, List<string>>? Resolve(string title)
        {
            try
            {
                var url = "https://catalog.gog.com/v1/catalog?limit=10&order=desc%3Ascore&query=" + Uri.EscapeDataString(title);
                var json = Client.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
                    return null;

                var want = TitleNormalizer.Normalize(title);
                foreach (var product in products.EnumerateArray())
                {
                    var name = product.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    if (TitleNormalizer.Normalize(name) != want)
                        continue;

                    var result = new Dictionary<string, List<string>>();
                    if (product.TryGetProperty("coverVertical", out var cover) && cover.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(cover.GetString()))
                        result["grids"] = new() { cover.GetString()! };

                    string? hero = null;
                    if (product.TryGetProperty("galaxyBackgroundImage", out var bg) && bg.ValueKind == JsonValueKind.String)
                        hero = bg.GetString();
                    if (string.IsNullOrEmpty(hero) && product.TryGetProperty("coverHorizontal", out var ch) && ch.ValueKind == JsonValueKind.String)
                        hero = ch.GetString();
                    if (!string.IsNullOrEmpty(hero))
                        result["heroes"] = new() { hero! };

                    if (product.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(logo.GetString()))
                        result["logos"] = new() { logo.GetString()! };

                    return result.Count > 0 ? result : null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Info($"GOG search failed for '{title}': {ex.Message}");
            }
            return null;
        }
    }
}
