using System.Collections.Generic;
using System.IO;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>
/// Desktop Win32 games the launcher scanners cannot see (currently Roblox Studio,
/// which opens standalone with <c>-ide</c>).
/// </summary>
public sealed class DesktopScanner : IScanner
{
    public string Name => GameSource.Desktop;

    public IReadOnlyList<GameInfo> Scan()
    {
        var result = new List<GameInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var versionsDir = Path.Combine(localAppData, "Roblox", "Versions");
        if (!Directory.Exists(versionsDir))
            return result;

        var versions = Directory.EnumerateDirectories(versionsDir)
            .OrderByDescending(Directory.GetLastWriteTimeUtc);

        foreach (var dir in versions)
        {
            var studio = Path.Combine(dir, "RobloxStudioBeta.exe");
            if (File.Exists(studio))
            {
                result.Add(new GameInfo
                {
                    Name = "Roblox Studio",
                    Exe = studio,
                    StartDir = dir,
                    LaunchOptions = "-ide",
                    Source = GameSource.Desktop,
                    SourceId = "roblox-studio",
                    Tags = { "Roblox" },
                });
                break;
            }
        }

        return result;
    }
}
