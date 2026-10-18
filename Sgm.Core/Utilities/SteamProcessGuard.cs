using System.Diagnostics;

namespace Sgm.Core.Utilities;

/// <summary>Detects whether Steam is currently running (guard before writing shortcuts.vdf).</summary>
public static class SteamProcessGuard
{
    public static bool IsSteamRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("steam");
            if (processes.Length == 0)
                return false;
            foreach (var p in processes)
                p.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
