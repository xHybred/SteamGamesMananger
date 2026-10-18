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
/// </summary>
public sealed class XboxScanner : IScanner
{
    private const string Explorer = @"C:\Windows\explorer.exe";

    public string Name => GameSource.Xbox;

    private static readonly string PowerQuery =
        "Get-AppxPackage | ForEach-Object {" +
        " $m = $null; try { $m = Get-AppxPackageManifest -Package $_.PackageFullName } catch {};" +
        " $ids = @(); $exts = @(); if ($m) {" +
        " $ids = @($m.Package.Applications.Application | ForEach-Object { $_.Id });" +
        " $exts = @($m.Package.Applications.Application.Extensions.Extension | ForEach-Object { $_.Category } | Where-Object { $_ }) };" +
        " [PSCustomObject]@{ Name=$_.Name; PackageFamilyName=$_.PackageFamilyName;" +
        " PackageFullName=$_.PackageFullName; InstallLocation=$_.InstallLocation;" +
        " AppIds=($ids -join ','); Exts=(($exts | Select-Object -Unique) -join ','); Manifest=[bool]$m } } | ConvertTo-Json -Compress";

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

    public static string? ManifestAppId(string install)
    {
        var manifest = Path.Combine(install, "AppxManifest.xml");
        if (!File.Exists(manifest))
            return null;
        try
        {
            var doc = XDocument.Load(manifest);
            return doc.Descendants().FirstOrDefault(el => el.Name.LocalName == "Application")?.Attribute("Id")?.Value;
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> ManifestArt(string install)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(Path.Combine(install, "AppxManifest.xml"));
        }
        catch
        {
            return new Dictionary<string, string>();
        }

        var visual = doc.Descendants().FirstOrDefault(el => el.Name.LocalName == "VisualElements");
        if (visual == null)
            return new Dictionary<string, string>();

        foreach (var key in new[] { "Square310x310Logo", "Square150x150Logo" })
        {
            var rel = visual.Attribute(key)?.Value.Trim() ?? "";
            if (rel.Length == 0)
                continue;
            var exact = Path.Combine(install, rel);
            if (File.Exists(exact))
                return new Dictionary<string, string> { ["squares"] = exact };

            var dir = Path.GetDirectoryName(exact)!;
            var stem = Path.GetFileNameWithoutExtension(exact);
            var ext = Path.GetExtension(exact);
            var scaled = Directory.EnumerateFiles(dir, $"{stem}.scale-*{ext}").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (scaled.Count > 0)
                return new Dictionary<string, string> { ["squares"] = scaled[^1] };
        }

        return new Dictionary<string, string>();
    }

    public IReadOnlyList<GameInfo> Scan()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<GameInfo>();
        if (Environment.GetEnvironmentVariable("SGM_SKIP_XBOX") == "1")
            return Array.Empty<GameInfo>();

        var result = new List<GameInfo>();
        var packageNames = new HashSet<string>();
        var rootArt = GamingRoot.GamingRootArt();

        foreach (var pkg in QueryPackages())
        {
            try
            {
                var family = GetString(pkg, "PackageFamilyName").Trim();
                var name = GetString(pkg, "Name").Trim();
                var install = GetString(pkg, "InstallLocation").Trim();
                var full = GetString(pkg, "PackageFullName").Trim();
                var hasManifest = pkg.TryGetProperty("Manifest", out var m) && m.ValueKind == JsonValueKind.True;

                if (family.Length == 0 || XboxRules.ShouldSkip(family, name)
                    || XboxRules.IsUtility(name, GetString(pkg, "Exts"), hasManifest))
                    continue;

                var appIds = GetString(pkg, "AppIds").Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                if (appIds.Count == 0)
                    appIds.Add(ManifestAppId(install) ?? "App");

                Dictionary<string, string> art;
                if (rootArt.TryGetValue(name.ToLowerInvariant(), out var localArt))
                    art = new Dictionary<string, string>(localArt);
                else
                    art = ManifestArt(install);

                foreach (var appId in appIds)
                {
                    var label = XboxRules.FriendlyName(name);
                    if (appIds.Count > 1)
                        label = $"{label} ({appId})";

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
}
