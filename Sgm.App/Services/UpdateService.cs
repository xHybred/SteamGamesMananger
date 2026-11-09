using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sgm.Core.Logging;

namespace Sgm.App.Services;

/// <summary>A newer release with a downloadable setup executable.</summary>
public sealed record UpdateInfo(
    Version Version,
    string AssetName,
    Uri DownloadUrl,
    long Size,
    string? Sha256);

/// <summary>
/// Checks GitHub releases for a newer version and downloads the setup executable.
/// The app version comes from the assembly version set in Sgm.App.csproj.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/xHybred/SteamGamesMananger/releases/latest";

    private static readonly HttpClient ApiHttp = CreateApiClient();
    private static readonly HttpClient DownloadHttp = CreateDownloadClient();

    public static Version CurrentVersion { get; } =
        Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0, 0));

    /// <summary>Returns the newest release when it is newer and has a setup executable.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await ApiHttp.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream, cancellationToken: cancellationToken);

        if (release is null || release.Draft || release.Prerelease)
            return null;

        if (!TryParseTag(release.TagName, out var latest) || latest <= CurrentVersion)
            return null;

        var asset = release.Assets.FirstOrDefault(a => IsInstallerAsset(a.Name));
        if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl))
        {
            AppLog.Warn($"Update {release.TagName} has no setup executable; ignoring it.");
            return null;
        }

        return new UpdateInfo(
            latest,
            asset.Name,
            downloadUrl,
            asset.Size,
            Sha256FromDigest(asset.Digest));
    }

    /// <summary>Downloads the installer and verifies the release checksum when one is published.</summary>
    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamGamesManager", "updates");
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, update.AssetName);
        var partial = destination + ".part";

        try
        {
            using var response = await DownloadHttp.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? update.Size;
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                             partial, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total > 0)
                        progress?.Report(Math.Clamp((double)received / total, 0, 1));
                }
            }

            if (total > 0 && received != total)
            {
                throw new InvalidDataException(
                    "The download ended before the installer was complete.");
            }

            if (update.Size > 0 && new FileInfo(partial).Length != update.Size)
            {
                throw new InvalidDataException(
                    "The downloaded installer size did not match the release metadata.");
            }

            if (update.Sha256 is not null &&
                !await HasExpectedHashAsync(partial, update.Sha256, cancellationToken))
            {
                throw new InvalidDataException(
                    "The downloaded installer did not match the published checksum.");
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(1);
            return destination;
        }
        catch
        {
            try
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
            catch
            {
                // Best-effort cleanup; the original error is more useful.
            }
            throw;
        }
    }

    /// <summary>
    /// Runs the setup silently, closing this app as needed. The installer manifest
    /// requests elevation and the setup relaunches the app when it finishes.
    /// </summary>
    public static void LaunchInstaller(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The downloaded installer was not found.", path);

        var directory = Path.GetDirectoryName(path) ?? Path.GetTempPath();
        var logPath = Path.Combine(directory, "install.log");
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = directory,
            Arguments =
                $"/SP- /SILENT /NOCANCEL /NORESTART " +
                $"/CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS " +
                $"/RELAUNCH /LOG=\"{logPath}\"",
        };

        Process.Start(startInfo);
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamGamesManager");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamGamesManager");
        return client;
    }

    private static bool IsInstallerAsset(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("setup", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseTag(string tag, out Version version)
    {
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        // Stable releases do not normally carry a suffix, but tolerate one.
        var suffix = text.IndexOf('-');
        if (suffix >= 0)
            text = text[..suffix];

        if (!Version.TryParse(text, out var parsed))
        {
            version = new Version(0, 0, 0, 0);
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static string? Sha256FromDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        var split = digest.IndexOf(':');
        if (split <= 0 || split == digest.Length - 1)
            return null;

        return digest[..split].Equals("sha256", StringComparison.OrdinalIgnoreCase)
            ? digest[(split + 1)..]
            : null;
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
