using CycloneHQ.Core.Models;
using CycloneHQ.Sync.Mfl;

namespace CycloneHQ.Sync.Enrichment;

// Usage and ranking data from outside the league, keyed by MFL player id.
sealed record PlayerExtras(PlayerUsage? Usage, int? ConsensusPositionRank, double? TradeValue, string? ValueSource);

static class MflRankValues
{
    // Baseline values from MFL's weekly player ranks, used when no better source has a player.
    // Rank 1 -> 100, rank 60 -> ~37, rank 150 -> ~8: a smooth curve comparable across positions.
    public static Dictionary<string, PlayerExtras> From(MflSeason season)
    {
        var positionRanks = season.OverallRanks
            .Where(r => season.Players.ContainsKey(r.Key))
            .GroupBy(r => season.Players[r.Key].Position)
            .SelectMany(g => g.OrderBy(r => r.Value).Select((r, i) => (r.Key, PosRank: i + 1)))
            .ToDictionary(x => x.Key, x => x.PosRank);

        return season.OverallRanks.ToDictionary(
            r => r.Key,
            r => new PlayerExtras(
                Usage: null,
                ConsensusPositionRank: positionRanks.GetValueOrDefault(r.Key) is > 0 and var pr ? pr : null,
                TradeValue: Math.Round(100 * Math.Exp(-(r.Value - 1) / 60.0), 1),
                ValueSource: "MFL player ranks"));
    }
}
