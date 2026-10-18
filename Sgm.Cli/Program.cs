using System.Diagnostics;
using Sgm.Core.Artwork;
using Sgm.Core.Configuration;
using Sgm.Core.Logging;
using Sgm.Core.Models;
using Sgm.Core.Scanners;
using Sgm.Core.Services;
using Sgm.Core.Steam;
using Sgm.Core.Utilities;

namespace Sgm.Cli;

internal static class Program
{
    private const string KnownLaunchers = "epic,gog,xbox,ubisoft,ea,battlenet,desktop";

    private sealed class Options
    {
        public string? SteamDir;
        public List<string> UserIds = new();
        public string Launchers = KnownLaunchers;
        public string? ApiKey;
        public bool NoArtwork;
        public bool DryRun;
        public bool Force;
        public bool Overwrite;
        public bool Prune;
        public string? Include;
        public string? EpicManifests;
        public string? GogDb;
        public bool Gui;
        public bool Help;
        public bool ShowVersion;
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine("steam-games-manager (Sgm.Cli) 2.0");
            return 0;
        }

        AppLog.Message += line => Console.WriteLine(line);

        if (options.Gui)
            return LaunchGui();

        var steamDir = SteamPaths.FindInstallDir(options.SteamDir);
        if (steamDir == null)
        {
            AppLog.Error("Steam installation not found. Pass --steam-dir explicitly.");
            return 2;
        }
        AppLog.Info($"Steam dir: {steamDir}");

        List<string> userDirs;
        try
        {
            userDirs = SteamPaths.ResolveAllUserDirs(steamDir, options.UserIds.Count > 0 ? options.UserIds : null);
        }
        catch (FileNotFoundException ex)
        {
            AppLog.Error(ex.Message);
            return 2;
        }

        var users = SteamUsers.ReadLoginUsers(steamDir);
        var recent = SteamUsers.MostRecentAccountId(steamDir);
        if (recent != null)
            AppLog.Info($"Most-recently-used account: {recent}");
        AppLog.Info($"Target accounts: {string.Join(", ", userDirs.Select(d => SteamWriter.AccountLabel(steamDir, d, users)))}");

        var scanner = new GameScanner(options.EpicManifests, options.GogDb);
        var games = Scanning.Filter(scanner.Scan(SplitLaunchers(options.Launchers)), options.Include);
        if (games.Count == 0 && !options.Prune)
        {
            AppLog.Warn("No games detected — nothing to do.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Detected {games.Count} game(s):");
        foreach (var g in games)
            Console.WriteLine($"  [{g.Source}] {g.Name}\n           exe={g.Exe}\n           opts={g.LaunchOptions}");

        if (options.DryRun)
        {
            foreach (var d in userDirs)
            {
                try
                {
                    var data = Sgm.Core.Vdf.ShortcutStore.Load(SteamPaths.ShortcutsPath(d));
                    var existing = data.TryGetValue("shortcuts", out var s) && s is Dictionary<string, object> dict ? dict.Count : 0;
                    Console.WriteLine($"Dry run [{Path.GetFileName(d)}]: would merge {games.Count} game(s) into {existing} existing. No changes written.");
                }
                catch (InvalidDataException ex)
                {
                    Console.WriteLine($"Dry run [{Path.GetFileName(d)}]: {ex.Message}");
                }
            }
            return 0;
        }

        if (SteamProcessGuard.IsSteamRunning())
        {
            const string msg = "Steam appears to be RUNNING. Close Steam before writing shortcuts.vdf to avoid corruption.";
            if (options.Force)
                AppLog.Warn($"{msg} Proceeding due to --force.");
            else
            {
                AppLog.Error($"{msg} Re-run with Steam closed (or --force to override at your own risk).");
                return 4;
            }
        }

        var failed = false;
        foreach (var d in userDirs)
        {
            var res = SteamWriter.WriteAccount(d, games, options.Overwrite, options.Prune);
            if (res.Error != null)
            {
                AppLog.Error($"[{Path.GetFileName(d)}] {res.Error}");
                failed = true;
                continue;
            }
            Console.WriteLine($"[{SteamWriter.AccountLabel(steamDir, d, users)}] +{res.Added} added, " +
                              $"~{res.Updated} updated, ={res.Skipped} skipped, -{res.Pruned} pruned.");
            if (res.Backup != null)
                Console.WriteLine($"  Backup: {res.Backup}");
        }

        if (options.NoArtwork || games.Count == 0)
        {
            Console.WriteLine("Restart Steam to see new shortcuts.");
            return failed ? 1 : 0;
        }

        var key = options.ApiKey ?? Environment.GetEnvironmentVariable("STEAMGRIDDB_API_KEY");
        SteamGridDbClient? client = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            try
            {
                client = new SteamGridDbClient(key);
            }
            catch (ArgumentException ex)
            {
                AppLog.Error(ex.Message);
                return 2;
            }
        }
        else
        {
            Console.WriteLine("No SteamGridDB key — using fully automatic store artwork.");
        }

        var cache = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var d in userDirs)
        {
            var result = ArtworkService.ForAccount(client, games, d, cache);
            cache = result.Cache;
            Console.WriteLine($"[{Path.GetFileName(d)}] Artwork: {result.Ok} succeeded, {result.Fail} without art. " +
                              $"Grid dir: {SteamPaths.GridDir(d)}");
        }
        Console.WriteLine("Restart Steam to see new shortcuts and artwork.");
        return failed ? 1 : 0;
    }

    private static IReadOnlyCollection<string> SplitLaunchers(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToList();

    private static int LaunchGui()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "SteamGamesManager.exe");
        if (!File.Exists(exe))
        {
            AppLog.Error($"GUI not found next to the CLI ({exe}). Build/run the Sgm.App project instead.");
            return 2;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        return 0;
    }

    private static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next(string name)
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"{name} requires a value");
                return args[++i];
            }

            switch (a)
            {
                case "-h" or "--help":
                    o.Help = true;
                    break;
                case "--version":
                    o.ShowVersion = true;
                    break;
                case "--steam-dir":
                    o.SteamDir = Next(a);
                    break;
                case "--user-id":
                    o.UserIds.Add(Next(a));
                    break;
                case "--launchers":
                    o.Launchers = Next(a);
                    break;
                case "--api-key":
                    o.ApiKey = Next(a);
                    break;
                case "--no-artwork":
                    o.NoArtwork = true;
                    break;
                case "--dry-run":
                    o.DryRun = true;
                    break;
                case "--force":
                    o.Force = true;
                    break;
                case "--overwrite":
                    o.Overwrite = true;
                    break;
                case "--prune":
                    o.Prune = true;
                    break;
                case "--include":
                    o.Include = Next(a);
                    break;
                case "--epic-manifests":
                    o.EpicManifests = Next(a);
                    break;
                case "--gog-db":
                    o.GogDb = Next(a);
                    break;
                case "--gui":
                    o.Gui = true;
                    break;
                default:
                    if (a.StartsWith("--launchers=", StringComparison.Ordinal))
                        o.Launchers = a["--launchers=".Length..];
                    else
                        throw new ArgumentException($"unknown option '{a}' (see --help)");
                    break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "steam-games-manager — import Epic/GOG/Xbox/Ubisoft/EA/Battle.net games into\n" +
            "Steam shortcuts.vdf with SteamGridDB (or automatic) artwork.\n\n" +
            "Usage: sgm [options]\n\n" +
            "  --steam-dir DIR        Steam install dir (auto-detected otherwise)\n" +
            "  --user-id ID           Target account id (repeatable). Default: all accounts\n" +
            "  --launchers LIST       Comma list: " + KnownLaunchers + "\n" +
            "  --api-key KEY          SteamGridDB API key (or $STEAMGRIDDB_API_KEY)\n" +
            "  --no-artwork           Skip artwork downloads\n" +
            "  --dry-run              Scan + report without writing\n" +
            "  --force                Write even if Steam is running (risky)\n" +
            "  --overwrite            Update already-imported shortcuts\n" +
            "  --prune                Remove OS/system UWP junk entries before merging\n" +
            "  --include TEXT         Only import games whose name contains this text\n" +
            "  --epic-manifests DIR   Override Epic Manifests dir\n" +
            "  --gog-db PATH          Override galaxy-2.0.db path\n" +
            "  --gui                  Launch the graphical interface\n" +
            "  -h, --help             Show this help\n");
    }
}
