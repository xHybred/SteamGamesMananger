using System.Collections.Generic;
using Sgm.Core.Logging;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>Runs the enabled launcher scanners and returns de-duplicated games.</summary>
public sealed class GameScanner
{
    private readonly string? _epicManifests;
    private readonly string? _gogDb;

    public GameScanner(string? epicManifests = null, string? gogDb = null)
    {
        _epicManifests = epicManifests;
        _gogDb = gogDb;
    }

    public IReadOnlyList<GameInfo> Scan(IReadOnlyCollection<string> launchers, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(launchers.Select(l => l.Trim().ToLowerInvariant()));
        var outGames = new List<GameInfo>();

        void Try(string source, Func<IReadOnlyList<GameInfo>> run)
        {
            if (!wanted.Contains(source))
                return;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var found = run();
                AppLog.Info($"{source}: {found.Count} game(s)");
                outGames.AddRange(found);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"{source} scan failed: {ex.Message}");
            }
        }

        Try(GameSource.Epic, () => new EpicScanner(_epicManifests).Scan());
        Try(GameSource.Gog, () => new GogScanner(db: _gogDb).Scan());
        Try(GameSource.Xbox, () => new XboxScanner().Scan());
        Try(GameSource.Ubisoft, () => UbisoftScanner.Scan());
        Try(GameSource.Ea, () => EaScanner.Scan());
        Try(GameSource.BattleNet, () => BattleNetScanner.Scan());
        Try(GameSource.Desktop, () => new DesktopScanner().Scan());

        var unknown = wanted.Where(l => !GameSource.Known.Contains(l)).ToList();
        if (unknown.Count > 0)
            AppLog.Warn($"Unknown launchers ignored: {string.Join(", ", unknown.OrderBy(x => x))}");

        return Scanning.DedupeTitles(outGames.Select(g => g.Normalized()));
    }
}
