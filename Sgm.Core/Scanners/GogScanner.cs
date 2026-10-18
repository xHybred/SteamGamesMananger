using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Sgm.Core.Models;
using Sgm.Core.Steam;

namespace Sgm.Core.Scanners;

/// <summary>
/// GOG Galaxy scanner: registry first, <c>galaxy-2.0.db</c> SQLite fallback,
/// with cover art from the Galaxy webcache (then the GamePieces URL).
/// </summary>
public sealed class GogScanner : IScanner
{
    private readonly string? _client;
    private readonly string? _db;

    public string Name => GameSource.Gog;

    public GogScanner(string? client = null, string? db = null)
    {
        _client = client;
        _db = db;
    }

    public static string GalaxyClient(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;
        var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        foreach (var dir in new[] { pf86, pf })
        {
            var cand = Path.Combine(dir, "GOG Galaxy", "GalaxyClient.exe");
            if (File.Exists(cand))
                return cand;
        }
        return Path.Combine(pf86, "GOG Galaxy", "GalaxyClient.exe");
    }

    public static string DbPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return SteamPaths.ExpandPath(explicitPath);
        var baseDir = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";
        return Path.Combine(baseDir, "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");
    }

    [SupportedOSPlatform("windows")]
    private static List<(string Id, string Name, string Path)> ScanRegistry()
    {
        var rows = new List<(string, string, string)>();
        foreach (var sub in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                using var key = baseKey.OpenSubKey(sub);
                if (key == null)
                    continue;

                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var game = key.OpenSubKey(name);
                        if (game == null)
                            continue;
                        var id = game.GetValue("gameID")?.ToString() ?? name;
                        var gname = game.GetValue("gameName")?.ToString() ?? name;
                        var path = game.GetValue("path")?.ToString() ?? "";
                        var depends = game.GetValue("dependsOn")?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(depends))
                            continue; // DLC / add-on
                        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                            continue; // stale registry entry
                        rows.Add((id, gname, path));
                    }
                    catch
                    {
                        // skip unreadable subkey
                    }
                }
            }
            catch
            {
                // missing hive/subkey
            }
        }
        return rows;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            Pooling = false,
        }.ToString();
        var con = new SqliteConnection(cs);
        con.Open();
        return con;
    }

    private static List<(string Id, string Title)> ScanSqlite(string db)
    {
        var rows = new List<(string, string)>();
        if (!File.Exists(db))
            return rows;

        try
        {
            using var con = OpenReadOnly(db);
            using var check = con.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='LibraryReleases'";
            if (check.ExecuteScalar() == null)
                return rows;

            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT releaseKey, title FROM LibraryReleases";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var title = (reader.IsDBNull(1) ? "" : reader.GetString(1)).Trim();
                if (title.Length == 0)
                    continue;
                var key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var id = key.Contains('_') ? key[(key.IndexOf('_') + 1)..] : key;
                rows.Add((id, title));
            }
        }
        catch (SqliteException)
        {
            // Unreadable / locked DB.
        }

        return rows;
    }

    /// <summary>Vertical covers per game id from the Galaxy webcache (then GamePieces URL).</summary>
    public static Dictionary<string, Dictionary<string, string>> GalaxyArt(string db)
    {
        var art = new Dictionary<string, Dictionary<string, string>>();
        if (!File.Exists(db))
            return art;

        SqliteConnection con;
        try
        {
            con = OpenReadOnly(db);
        }
        catch (SqliteException)
        {
            return art;
        }

        using (con)
        {
            try
            {
                var tables = new HashSet<string>(StringComparer.Ordinal);
                using (var tcmd = con.CreateCommand())
                {
                    tcmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                    using var reader = tcmd.ExecuteReader();
                    while (reader.Read())
                        tables.Add(reader.GetString(0));
                }

                if (tables.Contains("WebCache") && tables.Contains("WebCacheResources") && tables.Contains("WebCacheResourceTypes"))
                {
                    long vtype = 3;
                    using (var tcmd = con.CreateCommand())
                    {
                        tcmd.CommandText = "SELECT id FROM WebCacheResourceTypes WHERE type='verticalCover'";
                        if (tcmd.ExecuteScalar() is long id)
                            vtype = id;
                    }

                    var webcache = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(db)!)!, "webcache");
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT w.releaseKey, w.userId, r.filename FROM WebCache w " +
                                      "JOIN WebCacheResources r ON r.webCacheId = w.id " +
                                      "WHERE r.webCacheResourceTypeId = $t";
                    cmd.Parameters.AddWithValue("$t", vtype);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var userId = reader.GetValue(1).ToString() ?? "";
                        var filename = reader.GetString(2);
                        var id = key.Contains('_') ? key[(key.IndexOf('_') + 1)..] : key;
                        var path = Path.Combine(webcache, userId, "gog", id, filename);
                        if (File.Exists(path))
                        {
                            if (!art.TryGetValue(id, out var dict))
                            {
                                dict = new Dictionary<string, string>();
                                art[id] = dict;
                            }
                            dict.TryAdd("grids", path);
                        }
                    }
                }

                if (tables.Contains("GamePieces"))
                {
                    long ptype = 378;
                    if (tables.Contains("GamePieceTypes"))
                    {
                        using var tcmd = con.CreateCommand();
                        tcmd.CommandText = "SELECT id FROM GamePieceTypes WHERE type='originalImages'";
                        if (tcmd.ExecuteScalar() is long id)
                            ptype = id;
                    }

                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT releaseKey, value FROM GamePieces WHERE gamePieceTypeId = $t";
                    cmd.Parameters.AddWithValue("$t", ptype);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        string url;
                        try
                        {
                            using var doc = JsonDocument.Parse(value);
                            url = doc.RootElement.TryGetProperty("verticalCover", out var el) ? el.GetString() ?? "" : "";
                        }
                        catch (JsonException)
                        {
                            continue;
                        }
                        if (url.StartsWith("http", StringComparison.Ordinal))
                        {
                            var id = key.Contains('_') ? key[(key.IndexOf('_') + 1)..] : key;
                            art.TryAdd(id, new Dictionary<string, string>());
                            art[id].TryAdd("grids", url);
                        }
                    }
                }
            }
            catch (SqliteException)
            {
                // Partial art is fine.
            }
        }

        return art;
    }

    public IReadOnlyList<GameInfo> Scan()
    {
        var client = GalaxyClient(_client);
        var db = DbPath(_db);
        var art = GalaxyArt(db);
        var result = new List<GameInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (id, gname, gpath) in ScanRegistry())
        {
            if (id.Length == 0 || !seen.Add(id))
                continue;
            var g = new GameInfo
            {
                Name = gname.Trim().Length > 0 ? gname.Trim() : $"GOG {id}",
                Exe = client,
                StartDir = gpath.Trim(),
                LaunchOptions = $"/command=runGame /gameId={id}",
                Source = GameSource.Gog,
                SourceId = id,
                Tags = { "GOG" },
            };
            if (art.TryGetValue(id, out var a))
                foreach (var (k, v) in a)
                    g.ArtUrls[k] = v;
            result.Add(g);
        }

        foreach (var (id, title) in ScanSqlite(db))
        {
            if (!seen.Add(id))
                continue;
            var g = new GameInfo
            {
                Name = title,
                Exe = client,
                StartDir = Path.GetDirectoryName(client) ?? "",
                LaunchOptions = $"/command=runGame /gameId={id}",
                Source = GameSource.Gog,
                SourceId = id,
                Tags = { "GOG" },
            };
            if (art.TryGetValue(id, out var a))
                foreach (var (k, v) in a)
                    g.ArtUrls[k] = v;
            result.Add(g);
        }

        return result;
    }
}
