using System;
using System.IO;

namespace Sgm.Core.Configuration;

/// <summary>
/// Chooses writable locations for settings, logs and caches. A portable copy keeps
/// its files next to the executable; an installed copy under Program Files uses the
/// user's profile instead, because the install directory is read-only at runtime.
/// </summary>
public static class AppPaths
{
    public const string AppName = "SteamGamesManager";

    private static readonly Lazy<bool> ExeDirWritable = new(() => CanUseExeDir(AppContext.BaseDirectory));

    public static string UserDataDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetEnvironmentVariable("APPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dir = Path.Combine(baseDir, AppName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string LocalDataDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? UserDataDir();

        var dir = Path.Combine(baseDir, AppName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Path for a per-user file, kept beside a portable exe when that is writable.</summary>
    public static string DataFile(string fileName)
    {
        var exeDir = AppContext.BaseDirectory;
        if (ExeDirWritable.Value)
            return Path.Combine(exeDir, fileName);

        return Path.Combine(LocalDataDir(), fileName);
    }

    /// <summary>Path for a per-user cache folder, kept beside a portable exe when that is writable.</summary>
    public static string CacheDirectory(string name)
    {
        var exeDir = AppContext.BaseDirectory;
        if (ExeDirWritable.Value)
        {
            var local = Path.Combine(exeDir, name);
            Directory.CreateDirectory(local);
            return local;
        }

        var dir = Path.Combine(LocalDataDir(), name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsUnderProgramFiles(string path)
    {
        var normalized = Path.GetFullPath(path);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CanUseExeDir(string exeDir)
    {
        if (IsUnderProgramFiles(exeDir))
            return false;

        try
        {
            var probe = Path.Combine(exeDir, ".sgm_write_test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
