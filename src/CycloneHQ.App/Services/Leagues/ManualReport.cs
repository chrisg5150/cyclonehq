using CycloneHQ.Core.Models;

namespace CycloneHQ.App.Services.Leagues;

public sealed record ManualPlayerRow(DirectoryEntry Entry, int PositionRank, bool IsStarter);

public sealed record PositionBenchmark(
    string Position,
    int Starters,          // starters per team at this position
    double MyStarterPpg,   // average of his best N at the position
    double LeagueAvgPpg,   // average of the top (teams x N) players at the position: a typical starter
    double Gap,            // MyStarterPpg - LeagueAvgPpg
    double GapPct);

// A pasted league has no league-wide data, so it is judged against simple benchmarks built from directory.json:
// a typical starter at each position is the average of the top (teams x starters) players there.
public static class ManualReport
{
    public static readonly (string Pos, int Starters)[] DefaultLineup =
        [("QB", 1), ("RB", 2), ("WR", 3), ("TE", 1), ("K", 1), ("DEF", 1)];

    public static string Pos(string position) => RosterMatcher.IsDefense(position) ? "DEF" : position.ToUpperInvariant() == "PK" ? "K" : position.ToUpperInvariant();

    public static (IReadOnlyList<ManualPlayerRow> Rows, IReadOnlyList<PositionBenchmark> Benchmarks) Build(
        ManualLeague league, PlayerDirectory directory)
    {
        var byId = directory.Players.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var mine = league.Roster.Select(s => s.PlayerId is { } id && byId.TryGetValue(id, out var e) ? e : null)
            .OfType<DirectoryEntry>().DistinctBy(e => e.Id).ToList();

        // Position rank by points per game across the whole directory, used when no consensus rank is available.
        var ppgRank = directory.Players.GroupBy(p => Pos(p.Position))
            .SelectMany(g => g.OrderByDescending(p => p.PointsPerGame).Select((p, i) => (p.Id, Rank: i + 1)))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Rank);

        var starters = DefaultLineup.ToDictionary(x => x.Pos, x => x.Starters);
        var rows = mine.GroupBy(e => Pos(e.Position))
            .SelectMany(g =>
            {
                var n = starters.GetValueOrDefault(g.Key, 0);
                return g.OrderByDescending(e => e.PointsPerGame)
                    .Select((e, i) => new ManualPlayerRow(e, e.ConsensusPositionRank ?? ppgRank.GetValueOrDefault(e.Id), i < n));
            })
            .OrderBy(r => Array.FindIndex(DefaultLineup, x => x.Pos == Pos(r.Entry.Position)) is var i and >= 0 ? i : 99)
            .ThenByDescending(r => r.Entry.PointsPerGame)
            .ToList();

        var teams = Math.Clamp(league.Teams, 4, 32);
        var benchmarks = DefaultLineup.Select(slot =>
        {
            var pool = directory.Players.Where(p => Pos(p.Position) == slot.Pos && p.PointsPerGame > 0)
                .OrderByDescending(p => p.PointsPerGame).Take(teams * slot.Starters).ToList();
            var avg = pool.Count == 0 ? 0 : pool.Average(p => p.PointsPerGame);
            var my = mine.Where(e => Pos(e.Position) == slot.Pos).OrderByDescending(e => e.PointsPerGame)
                .Select(e => e.PointsPerGame).Concat(Enumerable.Repeat(0.0, slot.Starters)).Take(slot.Starters).Average();
            var gap = my - avg;
            return new PositionBenchmark(slot.Pos, slot.Starters, my, avg, gap, avg > 0 ? gap / avg : 0);
        }).ToList();

        return (rows, benchmarks);
    }
}
