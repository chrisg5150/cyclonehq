using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// Everything the engine knows about the league once players are rated and lineups are set.
public sealed class LeagueContext
{
    public required LeagueInfo League { get; init; }
    public required LineupRules Rules { get; init; }
    public required Dictionary<string, RatedPlayer> Players { get; init; }
    public required Dictionary<string, List<RatedPlayer>> Rosters { get; init; }     // franchise id -> players
    public required Dictionary<string, Lineup> Lineups { get; init; }
    public required Dictionary<string, int> BaseStarters { get; init; }              // position -> typical starters
    public required Dictionary<string, double> StarterBar { get; init; }             // position -> median last-starter score
    public required List<string> StrengthPositions { get; init; }
    public Dictionary<string, TeamProfile> Profiles { get; } = [];
    public Dictionary<string, List<SlotGap>> Slots { get; } = [];

    // Position -> 0..1 how much this team needs help there, from its below-median lineup slots.
    // The weakest slot is 1; other below-median slots 0.6. A shared slot (WR/TE, Flex) counts for every
    // position that can fill it.
    public Dictionary<string, double> NeedLevels(string franchiseId)
    {
        var levels = new Dictionary<string, double>();
        foreach (var (s, i) in WeakSlots(franchiseId).Select((s, i) => (s, i)))
            foreach (var p in s.Positions)
                levels[p] = Math.Max(levels.GetValueOrDefault(p), i == 0 ? 1.0 : 0.6);
        return levels;
    }

    readonly Dictionary<string, Dictionary<string, double>> _needLevels = [];
    public Dictionary<string, double> NeedLevelsCached(string franchiseId) =>
        _needLevels.TryGetValue(franchiseId, out var l) ? l : _needLevels[franchiseId] = NeedLevels(franchiseId);

    // Below-median lineup slots worth fixing by trade (not K), worst first.
    public List<SlotGap> WeakSlots(string franchiseId)
    {
        var n = Lineups.Count;
        return Slots[franchiseId]
            .Where(s => s.Gap < -0.05 && s.Rank > n / 2 && !s.Positions.All(PlayerValuation.NonTradePositions.Contains))
            .OrderBy(s => s.Rank > n * 2 / 3 ? 0 : 1).ThenBy(s => s.Gap)
            .ToList();
    }

    public SlotGap? WeakestSlot(string franchiseId) => WeakSlots(franchiseId).FirstOrDefault();

    // Positions that can fill a slot trailing the median by a real margin (a point a week or more):
    // only these justify proposing a trade with a smaller overall gain.
    public HashSet<string> UrgentNeedPositions(string franchiseId) =>
        WeakSlots(franchiseId).Where(s => s.Gap <= -1.0).SelectMany(s => s.Positions).ToHashSet();

    public string TeamName(string franchiseId) =>
        League.Franchises.FirstOrDefault(f => f.Id == franchiseId)?.Name ?? $"Team {franchiseId}";

    public IEnumerable<RatedPlayer> FreeAgents => Players.Values.Where(p => string.IsNullOrEmpty(OwnerOf(p.Id)));

    readonly Dictionary<string, string> _owners = [];
    public string? OwnerOf(string playerId) => _owners.GetValueOrDefault(playerId);

    public static LeagueContext Build(LeagueInfo league, PlayerPool pool)
    {
        var players = PlayerValuation.Rate(pool.Players);
        var rules = LineupRules.From(league.Lineup);
        var rosters = new Dictionary<string, List<RatedPlayer>>();
        var owners = new Dictionary<string, string>();

        foreach (var f in league.Franchises)
        {
            var ids = new HashSet<string>(f.RosterPlayerIds ?? []);
            foreach (var p in pool.Players.Where(p => p.FantasyOwnerId == f.Id)) ids.Add(p.Id);
            var roster = ids.Where(players.ContainsKey).Select(id => players[id]).ToList();
            rosters[f.Id] = roster;
            foreach (var r in roster) owners[r.Id] = f.Id;
        }

        var lineups = rosters.ToDictionary(kv => kv.Key, kv => LineupOptimizer.Best(kv.Value, rules));
        var strengthPositions = Positions.All.Where(p => rules.GroupFor(p) is not null)
            .Concat(rules.Positions.Where(p => !Positions.All.Contains(p)))
            .Distinct().ToList();

        var baseStarters = new Dictionary<string, int>();
        foreach (var pos in strengthPositions)
        {
            var avg = lineups.Count == 0 ? 1 : lineups.Values.Average(l => l.Starters.Count(s => s.Pos == pos));
            var g = rules.GroupFor(pos)!;
            var n = (int)Math.Round(avg, MidpointRounding.AwayFromZero);
            if (g.Positions.Count == 1) n = Math.Max(n, g.Min);
            baseStarters[pos] = Math.Max(1, n);
        }

        var bar = new Dictionary<string, double>();
        foreach (var pos in strengthPositions)
        {
            var k = baseStarters[pos];
            var lastStarters = rosters.Values
                .Select(r => r.Where(p => p.Pos == pos).OrderByDescending(p => p.Score).Skip(k - 1).FirstOrDefault()?.Score ?? 0)
                .ToList();
            bar[pos] = Median(lastStarters);
        }

        var ctx = new LeagueContext
        {
            League = league, Rules = rules, Players = players, Rosters = rosters, Lineups = lineups,
            BaseStarters = baseStarters, StarterBar = bar, StrengthPositions = strengthPositions,
        };
        foreach (var kv in owners) ctx._owners[kv.Key] = kv.Value;
        foreach (var t in rosters.Keys) ctx.Slots[t] = TeamProfiler.SlotGaps(ctx, t);
        TeamProfiler.Fill(ctx);
        return ctx;
    }

    public static double Median(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}

// One lineup slot ("QB #1", "WR/TE #3", "Flex") compared with the same slot on every other team.
public sealed record SlotGap(string Slot, IReadOnlySet<string> Positions, double Score, double Median, int Rank, string PlayerId)
{
    public double Gap => Score - Median;
}

public static class TeamProfiler
{
    // Slot-by-slot view of a team's best lineup: each group's required starters in order, then the flex spots.
    // This is how a fantasy player thinks about holes ("my WR3 is weak", "my flex is weak").
    public static List<SlotGap> SlotGaps(LeagueContext ctx, string franchiseId)
    {
        var slotsByTeam = ctx.Lineups.ToDictionary(kv => kv.Key, kv => Slots(ctx.Rules, kv.Value));
        var mine = slotsByTeam[franchiseId];
        var result = new List<SlotGap>();
        foreach (var (key, positions, player) in mine)
        {
            var all = slotsByTeam.Values.Select(s => s.FirstOrDefault(x => x.key == key).player?.Score ?? 0).ToList();
            var median = LeagueContext.Median(all);
            var rank = 1 + all.Count(v => v > player.Score + 1e-9);
            result.Add(new SlotGap(key, positions, player.Score, median, rank, player.Id));
        }
        return result;
    }

    static List<(string key, IReadOnlySet<string> positions, RatedPlayer player)> Slots(LineupRules rules, Lineup lineup)
    {
        var slots = new List<(string, IReadOnlySet<string>, RatedPlayer)>();
        var flex = new List<RatedPlayer>();
        foreach (var g in rules.Groups)
        {
            var label = string.Join("/", g.Positions);
            var starters = lineup.Starters.Where(s => g.Positions.Contains(s.Pos)).OrderByDescending(s => s.Score).ToList();
            for (var i = 0; i < starters.Count; i++)
            {
                if (i < g.Min) slots.Add((g.Min == 1 ? label : $"{label} #{i + 1}", g.Positions, starters[i]));
                else flex.Add(starters[i]);
            }
        }
        var flexPositions = rules.Groups.Where(g => g.Max > g.Min).SelectMany(g => g.Positions).ToHashSet();
        foreach (var (p, i) in flex.OrderByDescending(p => p.Score).Select((p, i) => (p, i)))
            slots.Add((i == 0 ? "Flex" : $"Flex #{i + 1}", flexPositions, p));
        return slots;
    }

    public static void Fill(LeagueContext ctx)
    {
        var teams = ctx.Rosters.Keys.ToList();
        var n = teams.Count;
        var bottomThirdStart = n - n / 3 + 1;  // 12 teams: ranks 9-12
        var topThird = Math.Max(1, n / 3);     // 12 teams: ranks 1-4

        // position -> team -> (starter score, depth score, first backup score)
        var scores = new Dictionary<string, Dictionary<string, (double starter, double depth, double backup)>>();
        foreach (var pos in ctx.StrengthPositions)
        {
            var k = ctx.BaseStarters[pos];
            scores[pos] = teams.ToDictionary(t => t, t =>
            {
                var at = ctx.Rosters[t].Where(p => p.Pos == pos).OrderByDescending(p => p.Score).ToList();
                var starter = at.Take(k).Sum(p => p.Score);
                var bench = at.Skip(k).ToList();
                var depth = bench.Take(3).Select((p, i) => p.Score * (i == 0 ? 1 : i == 1 ? 0.5 : 0.25)).Sum();
                return (starter, depth, bench.FirstOrDefault()?.Score ?? 0);
            });
        }

        // A position that shares a combined slot (TE in "WR+TE") is only a real need when the whole group is
        // weak; otherwise the team simply starts another WR there.
        var groupRank = new Dictionary<(SlotGroup, string), int>();
        foreach (var g in ctx.Rules.Groups.Where(g => g.Positions.Count > 1))
        {
            var k = Math.Max(1, g.Min);
            var gs = teams.ToDictionary(t => t, t => ctx.Rosters[t].Where(p => g.Positions.Contains(p.Pos))
                .OrderByDescending(p => p.Score).Take(k).Sum(p => p.Score));
            foreach (var t in teams) groupRank[(g, t)] = 1 + gs.Values.Count(v => v > gs[t] + 1e-9);
        }

        foreach (var t in teams)
        {
            var strengths = new List<PositionStrength>();
            var needs = new List<(string pos, double gap)>();
            var surplus = new List<string>();
            foreach (var pos in ctx.StrengthPositions)
            {
                var all = scores[pos];
                var mine = all[t];
                var rank = 1 + all.Values.Count(v => v.starter > mine.starter + 1e-9);
                var median = LeagueContext.Median(all.Values.Select(v => v.starter).ToList());
                strengths.Add(new PositionStrength(pos, R(mine.starter), rank, R(mine.starter - median), R(mine.depth)));

                var group = ctx.Rules.GroupFor(pos)!;
                var groupWeak = group.Positions.Count == 1 || groupRank[(group, t)] > n / 2;
                if (n >= 3 && rank >= bottomThirdStart && groupWeak) needs.Add((pos, mine.starter - median));
                var bar = ctx.StarterBar[pos];
                if (!PlayerValuation.NonTradePositions.Contains(pos) && bar > 0 &&
                    (mine.backup >= 0.9 * bar || (rank <= topThird && mine.backup >= 0.75 * bar)))
                    surplus.Add(pos);
            }
            var lineup = ctx.Lineups[t];
            ctx.Profiles[t] = new TeamProfile(
                t,
                lineup.Starters.Select(s => s.Id).ToList(),
                R(lineup.Points),
                strengths,
                needs.OrderBy(x => x.gap).Select(x => x.pos).ToList(),
                surplus);
        }
    }

    static double R(double x) => Math.Round(x, 2);
}
