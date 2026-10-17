using CycloneHQ.Core.Models;
using CycloneHQ.Sync.Enrichment;
using CycloneHQ.Sync.Mfl;

namespace CycloneHQ.Sync;

// directory.json: every fantasy-relevant NFL player, so a pasted roster from another
// platform can be matched by name. Points use the first synced league's scoring.
static class PlayerDirectoryBuilder
{
    public static PlayerDirectory Build(MflSeason season, LeagueSyncResult league, IReadOnlyDictionary<string, PlayerExtras> extras)
    {
        var entries = season.Players.Values
            .Where(p => p.HasNflTeam || league.AllStats.ContainsKey(p.Id))
            .Select(p =>
            {
                var extra = extras.GetValueOrDefault(p.Id);
                return new DirectoryEntry(
                    p.Id, p.Name, p.Position, p.NflTeam,
                    league.AllStats.GetValueOrDefault(p.Id)?.PointsPerGame ?? 0,
                    extra?.TradeValue ?? 0,
                    extra?.ConsensusPositionRank);
            })
            .OrderByDescending(e => e.TradeValue).ThenByDescending(e => e.PointsPerGame)
            .ToList();
        return new PlayerDirectory(DateTimeOffset.UtcNow, entries);
    }
}
