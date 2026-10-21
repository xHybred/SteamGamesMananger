using System.Collections.Generic;
using System.IO;
using Sgm.Core.Models;
using Sgm.Core.Steam;
using Sgm.Core.Vdf;

namespace Sgm.Core.Services;

public sealed record WriteResult(
    string UserDir,
    int Added,
    int Updated,
    int Skipped,
    int Pruned,
    string? Backup,
    string? Error);

/// <summary>Merges detected games into one account's <c>shortcuts.vdf</c>.</summary>
public static class SteamWriter
{
    public static WriteResult WriteAccount(string userDir, IReadOnlyList<GameInfo> games, bool overwrite = false, bool prune = false)
    {
        var path = SteamPaths.ShortcutsPath(userDir);
        try
        {
            Dictionary<string, object> data;
            try
            {
                data = ShortcutStore.Load(path);
            }
            catch (InvalidDataException ex)
            {
                return new WriteResult(userDir, 0, 0, 0, 0, null,
                    $"Corrupt shortcuts.vdf, skipped (restore from .bak): {ex.Message}");
            }

            var pruned = 0;
            if (prune)
                pruned = ShortcutStore.PruneSystemEntries(data);

            var stats = ShortcutStore.MergeGames(data, games, overwrite);
            var backup = ShortcutStore.Write(path, data);
            return new WriteResult(userDir, stats.Added, stats.Updated, stats.Skipped, pruned, backup, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WriteResult(userDir, 0, 0, 0, 0, null, $"Write failed: {ex.Message}");
        }
    }

    /// <summary>Remove the given games' shortcuts from an account. Returns (removed, backup, error).</summary>
    public static (int Removed, string? Backup, string? Error) RemoveAccount(string userDir, IReadOnlyList<GameInfo> games)
    {
        var path = SteamPaths.ShortcutsPath(userDir);
        try
        {
            var data = ShortcutStore.Load(path);
            var removed = ShortcutStore.RemoveGames(data, games);
            if (removed == 0)
                return (0, null, null);
            var backup = ShortcutStore.Write(path, data);
            return (removed, backup, null);
        }
        catch (InvalidDataException ex)
        {
            return (0, null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, null, ex.Message);
        }
    }

    /// <summary>Prune system/junk entries from an account. Returns (removed, backup, error).</summary>
    public static (int Removed, string? Backup, string? Error) PruneAccount(string userDir)
    {
        var path = SteamPaths.ShortcutsPath(userDir);
        try
        {
            var data = ShortcutStore.Load(path);
            var removed = ShortcutStore.PruneSystemEntries(data);
            if (removed == 0)
                return (0, null, null);
            var backup = ShortcutStore.Write(path, data);
            return (removed, backup, null);
        }
        catch (InvalidDataException ex)
        {
            return (0, null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, null, ex.Message);
        }
    }

    /// <summary>Human label for an account: userdata id + persona name when known.</summary>
    public static string AccountLabel(string steamDir, string userDir, IReadOnlyList<SteamLoginUser>? users = null)
    {
        var uid = Path.GetFileName(userDir);
        users ??= SteamUsers.ReadLoginUsers(steamDir);
        foreach (var u in users)
        {
            if (u.AccountId == uid)
            {
                var who = u.Persona.Length > 0 ? u.Persona : (u.Account.Length > 0 ? u.Account : "?");
                return $"{uid} ({who})";
            }
        }
        return uid;
    }
}
