using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Sgm.Core.Models;
using Sgm.Core.Scanners.Xbox;

namespace Sgm.Core.Scanners;

/// <summary>
/// Xbox / UWP scanner: enumerates AppX packages via PowerShell, splitting
/// Exe = explorer.exe from LaunchOptions = shell:AppsFolder\&lt;Family&gt;!&lt;AppId&gt;.
/// Only packages registered in a drive's ".GamingRoot" are treated as games, which keeps
/// Microsoft Store apps, frameworks, and resource packages out of the results.
/// </summary>
public sealed class XboxScanner : IScanner
{
    private const string Explorer = @"C:\Windows\explorer.exe";

    public string Name => GameSource.Xbox;

    private static readonly string PowerQuery =
        "Get-AppxPackage | ForEach-Object {" +
        " [PSCustomObject]@{ Name=$_.Name; PackageFamilyName=$_.PackageFamilyName;" +
        " PackageFullName=$_.PackageFullName; InstallLocation=$_.InstallLocation;" +
        " IsFramework=$_.IsFramework; IsResourcePackage=$_.IsResourcePackage } } | ConvertTo-Json -Compress";

    private static List<JsonElement> QueryPackages()
    {
        var result = new List<JsonElement>();
        Process proc;
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", PowerQuery })
                psi.ArgumentList.Add(arg);

            proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(120_000);
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return result;

            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    result.Add(el.Clone());
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                result.Add(doc.RootElement.Clone());
            }
        }
        catch (Exception)
        {
            return result;
        }

        return result;
    }

    /// <summary>Everything the scanner needs from one <c>AppxManifest.xml</c>.</summary>
    public sealed record XboxManifest(
        List<string> AppIds,
        string DisplayName,
        string Extensions,
        Dictionary<string, string> Art);

    public static XboxManifest ReadManifest(string install)
    {
        var empty = new XboxManifest(new List<string>(), "", "", new Dictionary<string, string>());
        XDocument doc;
        try
        {
            doc = XDocument.Load(Path.Combine(install, "AppxManifest.xml"));
        }
        catch
        {
            return empty;
        }

        var appIds = new List<string>();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var art = new Dictionary<string, string>();
        var display = "";

        foreach (var app in doc.Descendants().Where(el => el.Name.LocalName == "Application"))
        {
            var id = app.Attribute("Id")?.Value.Trim() ?? "";
            if (id.Length > 0 && !appIds.Contains(id))
                appIds.Add(id);

            var visual = app.Descendants().FirstOrDefault(el => el.Name.LocalName == "VisualElements");
            if (visual != null)
            {
                if (display.Length == 0)
                    display = CleanDisplayName(visual.Attribute("DisplayName")?.Value);
                if (art.Count == 0)
                    art = ManifestVisualArt(install, visual);
            }

            foreach (var extension in app.Descendants().Where(el => el.Name.LocalName == "Extension"))
            {
                var category = extension.Attribute("Category")?.Value.Trim() ?? "";
                if (category.Length > 0)
                    extensions.Add(category);
            }
        }

        if (display.Length == 0)
        {
            var properties = doc.Descendants().FirstOrDefault(el => el.Name.LocalName == "Properties");
            display = CleanDisplayName(properties?.Attribute("DisplayName")?.Value);
        }

        return new XboxManifest(appIds, display, string.Join(",", extensions), art);
    }

    private static string CleanDisplayName(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? "" : trimmed;
    }

    private static Dictionary<string, string> ManifestVisualArt(string install, XElement visual)
    {
        var art = new Dictionary<string, string>();

        var square = "";
        foreach (var key in new[] { "Square480x480Logo", "Square310x310Logo", "Square150x150Logo", "StoreLogo" })
        {
            square = ResolveAsset(install, visual.Attribute(key)?.Value);
            if (square.Length > 0)
                break;
        }
        if (square.Length > 0)
            art["squares"] = square;

        var splash = ResolveAsset(install, visual.Attribute("SplashScreenImage")?.Value);
        if (splash.Length > 0)
        {
            art["grids"] = splash;
            art["heroes"] = splash;
        }

        return art;
    }

    /// <summary>Resolve a manifest asset path, preferring the exact file then the highest scale variant.</summary>
    private static string ResolveAsset(string install, string? relative)
    {
        var rel = relative?.Trim() ?? "";
        if (rel.Length == 0)
            return "";
        var exact = Path.Combine(install, rel);
        if (File.Exists(exact))
            return exact;

        var dir = Path.GetDirectoryName(exact);
        if (dir == null || !Directory.Exists(dir))
            return "";
        var stem = Path.GetFileNameWithoutExtension(exact);
        var ext = Path.GetExtension(exact);
        try
        {
            var scaled = Directory.EnumerateFiles(dir, $"{stem}.scale-*{ext}").OrderBy(f => f, StringComparer.Ordinal).ToList();
            return scaled.Count > 0 ? scaled[^1] : "";
        }
        catch
        {
            return "";
        }
    }

    public static string? ManifestAppId(string install)
        => ReadManifest(install).AppIds.FirstOrDefault();

    public static Dictionary<string, string> ManifestArt(string install)
        => ReadManifest(install).Art;

    public IReadOnlyList<GameInfo> Scan()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<GameInfo>();
        if (Environment.GetEnvironmentVariable("SGM_SKIP_XBOX") == "1")
            return Array.Empty<GameInfo>();

        var result = new List<GameInfo>();
        var packageNames = new HashSet<string>();
        var identities = GamingRoot.InstalledIdentities();
        if (identities.Count == 0)
            return result;

        var rootArt = GamingRoot.GamingRootArt();

        foreach (var pkg in QueryPackages())
        {
            try
            {
                var family = GetString(pkg, "PackageFamilyName").Trim();
                var name = GetString(pkg, "Name").Trim();
                var install = GetString(pkg, "InstallLocation").Trim();
                var full = GetString(pkg, "PackageFullName").Trim();
                var isFramework = GetBool(pkg, "IsFramework");
                var isResourcePackage = GetBool(pkg, "IsResourcePackage");

                if (!XboxRules.IsCandidatePackage(name, family, isFramework, isResourcePackage))
                    continue;
                if (!XboxRules.IsRegisteredGame(name, identities) || !Directory.Exists(install))
                    continue;
                if (XboxRules.ShouldSkip(family, name))
                    continue;

                var manifest = ReadManifest(install);
                if (manifest.AppIds.Count == 0)
                    continue;
                if (XboxRules.IsUtility(name, manifest.Extensions, hasManifest: true))
                    continue;

                Dictionary<string, string> art;
                if (rootArt.TryGetValue(name.ToLowerInvariant(), out var localArt))
                    art = new Dictionary<string, string>(localArt);
                else
                    art = manifest.Art;

                var display = manifest.DisplayName.Length > 0 ? manifest.DisplayName : XboxRules.FriendlyName(name);
                foreach (var appId in manifest.AppIds)
                {
                    var label = display;
                    if (manifest.AppIds.Count > 1)
                        label = $"{display} ({appId})";

                    var g = new GameInfo
                    {
                        Name = label,
                        Exe = Explorer,
                        StartDir = @"C:\Windows",
                        LaunchOptions = $@"shell:AppsFolder\{family}!{appId}",
                        Source = GameSource.Xbox,
                        SourceId = full.Length > 0 ? full : family,
                        Tags = { "Xbox", "UWP" },
                    };
                    foreach (var (k, v) in art)
                        g.ArtUrls[k] = v;
                    result.Add(g);
                    packageNames.Add(name.ToLowerInvariant());
                }
            }
            catch
            {
                // skip malformed package
            }
        }

        var seen = new HashSet<(string, string)>(result.Select(g => (g.Name.ToLowerInvariant(), g.Exe.ToLowerInvariant())));
        foreach (var game in GamingRoot.ScanGamingRoots(packageNames))
        {
            if (seen.Add((game.Name.ToLowerInvariant(), game.Exe.ToLowerInvariant())))
                result.Add(game);
        }

        return result;
    }

    private static string GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private static bool GetBool(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True;
}
