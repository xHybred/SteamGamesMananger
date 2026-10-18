using System.Diagnostics;
using Sgm.Core.Utilities;

namespace Sgm.Core.Steam;

/// <summary>Launch, close, and restart Steam without involving the UI.</summary>
public static class SteamControl
{
    /// <summary>Full path to <c>steam.exe</c> for the installation, or null when it is missing.</summary>
    public static string? SteamExePath(string? steamDir)
    {
        if (string.IsNullOrWhiteSpace(steamDir))
            return null;
        var exe = Path.Combine(steamDir, "steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Start Steam. Returns false when <c>steam.exe</c> cannot be located.</summary>
    public static bool StartSteam(string? steamDir)
    {
        var exe = SteamExePath(steamDir);
        if (exe == null)
            return false;

        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = steamDir!,
        });
        return true;
    }

    /// <summary>
    /// Ask Steam to shut down (graceful <c>-shutdown</c>) and wait for it to exit.
    /// Returns whether Steam is no longer running when this completes.
    /// </summary>
    public static async Task<bool> ShutdownSteamAsync(string? steamDir, int timeoutMs = 20000)
    {
        var exe = SteamExePath(steamDir);
        if (exe != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, "-shutdown")
                {
                    UseShellExecute = true,
                    WorkingDirectory = steamDir!,
                });
            }
            catch
            {
                // Fall through to waiting below.
            }
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (SteamProcessGuard.IsSteamRunning() && Environment.TickCount64 < deadline)
            await Task.Delay(500).ConfigureAwait(false);

        return !SteamProcessGuard.IsSteamRunning();
    }

    /// <summary>Close Steam if needed, then start it again.</summary>
    public static async Task<bool> RestartSteamAsync(string? steamDir, int timeoutMs = 20000)
    {
        await ShutdownSteamAsync(steamDir, timeoutMs).ConfigureAwait(false);
        return StartSteam(steamDir);
    }
}
