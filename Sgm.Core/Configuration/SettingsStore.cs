using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sgm.Core.Models;

namespace Sgm.Core.Configuration;

/// <summary>
/// JSON settings persistence next to the exe, with a %APPDATA% fallback.
/// Tolerant of missing/corrupt files and unknown values.
/// </summary>
public sealed class SettingsStore
{
    public const string AppName = "SteamGamesManager";
    public const string FileName = "settings.json";

    private static readonly string[] ValidViews = { "list", "grid" };
    private static readonly string[] ValidThemes = { "steam", "gray" };
    private static readonly string[] ValidDensities = { "compact", "normal", "large" };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string Path { get; }

    public SettingsStore(string? path = null) => Path = path ?? DefaultPath();

    public static string DefaultPath()
    {
        var exeDir = AppContext.BaseDirectory;
        if (IsWritable(exeDir))
            return System.IO.Path.Combine(exeDir, FileName);

        var cwd = Directory.GetCurrentDirectory();
        if (IsWritable(cwd))
            return System.IO.Path.Combine(cwd, FileName);

        var baseDir = Environment.GetEnvironmentVariable("APPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = System.IO.Path.Combine(baseDir, AppName);
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, FileName);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = System.IO.Path.Combine(dir, ".sgm_write_test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AppSettings Load(AppSettings? defaults = null)
    {
        var state = defaults?.Clone() ?? new AppSettings();
        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (IOException)
        {
            return state;
        }
        catch (UnauthorizedAccessException)
        {
            return state;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return state;
        }

        if (root is not JsonObject obj)
            return state;

        if (obj["sources"] is JsonObject sources)
        {
            foreach (var key in AppSettings.DefaultSources().Keys)
            {
                if (sources[key] is JsonValue sv && sv.TryGetValue(out bool val))
                    state.Sources[key] = val;
            }
        }

        state.View = ReadString(obj, "view", state.View);
        state.Density = ReadString(obj, "density", state.Density);
        state.ApiKey = ReadString(obj, "api_key", state.ApiKey);
        state.Theme = ReadString(obj, "theme", state.Theme);

        if (obj["artwork"] is JsonValue av && av.TryGetValue(out bool artwork)) state.Artwork = artwork;
        if (obj["overwrite"] is JsonValue ov && ov.TryGetValue(out bool overwrite)) state.Overwrite = overwrite;
        if (obj["force"] is JsonValue fv && fv.TryGetValue(out bool force)) state.Force = force;

        if (!ValidViews.Contains(state.View)) state.View = "list";
        if (!ValidThemes.Contains(state.Theme)) state.Theme = "steam";
        if (!ValidDensities.Contains(state.Density)) state.Density = "compact"; // legacy "roomy" maps here

        if (obj["hidden"] is JsonArray hidden)
        {
            state.Hidden = hidden
                .OfType<JsonArray>()
                .Where(a => a.Count == 3)
                .Select(a => a.Select(n => n?.ToString() ?? "").ToArray())
                .ToList();
        }

        return state;
    }

    private static string ReadString(JsonObject obj, string key, string fallback)
        => obj[key] is JsonValue v && v.TryGetValue(out string? s) && s != null ? s : fallback;

    public void Save(AppSettings state)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);

            // Preserve unknown/extra keys already present, then overlay known state.
            JsonObject merged = new();
            if (File.Exists(Path))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(Path)) is JsonObject current)
                        merged = current;
                }
                catch (JsonException)
                {
                    // ignore, overwrite
                }
            }

            merged["sources"] = new JsonObject(state.Sources.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))));
            merged["view"] = state.View;
            merged["density"] = state.Density;
            merged["artwork"] = state.Artwork;
            merged["overwrite"] = state.Overwrite;
            merged["force"] = state.Force;
            merged["api_key"] = state.ApiKey;
            merged["theme"] = state.Theme;
            merged["hidden"] = new JsonArray(state.Hidden
                .Select(h => (JsonNode)new JsonArray(h.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()))
                .ToArray());

            File.WriteAllText(Path, merged.ToJsonString(WriteOptions));
        }
        catch (IOException)
        {
            // Best-effort; the GUI surfaces failures elsewhere.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
