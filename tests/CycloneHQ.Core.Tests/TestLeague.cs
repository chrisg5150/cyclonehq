using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Tests;

// Small builder for hand-made leagues: every scoring input equals the given points per game, so expectations stay obvious.
public sealed class TestLeague
{
    public static readonly IReadOnlyList<LineupSlot> UlaffLineup =
        [new("QB", 1, 1), new("RB", 1, 2), new("WR+TE", 3, 4), new("PK", 1, 1)];

    readonly List<Player> _players = [];
    readonly Dictionary<string, List<string>> _rosters = [];
    int _next = 1;

    public string Add(string team, string pos, double ppg, double value, string? name = null)
    {
        var id = (_next++).ToString("0000");
        _players.Add(new Player(id, name ?? $"{team} {pos} {id}", pos, "NFL", 25, 14, team, null, null,
            new PlayerStats(6, ppg * 6, ppg, ppg, ppg, []), null, new PlayerRank(null, null, value, "test"), []));
        if (!_rosters.TryGetValue(team, out var r)) _rosters[team] = r = [];
        r.Add(id);
        return id;
    }

    // Gives a team a full, average roster so the league has a sensible median.
    public void AddFillerTeam(string team)
    {
        Add(team, "QB", 18, 50);
        Add(team, "RB", 13, 45);
        Add(team, "RB", 9, 25);
        Add(team, "WR", 13, 45);
        Add(team, "WR", 11, 35);
        Add(team, "WR", 8, 20);
        Add(team, "TE", 8, 20);
        Add(team, "PK", 8, 3);
    }

    public static readonly IReadOnlyList<LineupSlot> StandardLineup =
        [new("QB", 1, 1), new("RB", 2, 2), new("WR", 2, 2), new("TE", 1, 1), new("PK", 1, 1)];

    public LeagueInfo League(IReadOnlyList<LineupSlot>? lineup = null) => new(
        "test", "Test League", 2026, 7, lineup ?? UlaffLineup, new ScoringSummary(1, 4, "PPR"),
        _rosters.Select(kv => new Franchise(kv.Key, $"Team {kv.Key}", null, null, 3, 3, 0, 600, 600, 1, kv.Value)).ToList(),
        DateTimeOffset.UtcNow);

    public PlayerPool Pool() => new(DateTimeOffset.UtcNow, _players);
}
