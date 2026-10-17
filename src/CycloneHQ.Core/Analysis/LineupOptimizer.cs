using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// One lineup group: a single position ("QB") or a combined one ("WR+TE"), with how many may start.
public sealed record SlotGroup(string Label, IReadOnlySet<string> Positions, int Min, int Max);

public sealed class LineupRules
{
    public required IReadOnlyList<SlotGroup> Groups { get; init; }
    public required int TotalStarters { get; init; }

    public const int DefaultTotalStarters = 7;

    // MFL style: per-position min/max plus an overall starter count. A slot named TOTAL or STARTERS
    // carries the count when the source supplies one; otherwise 7 (this league), clamped to what the slots allow.
    public static LineupRules From(IReadOnlyList<LineupSlot>? slots, int? totalStarters = null)
    {
        var groups = new List<SlotGroup>();
        int? total = totalStarters;
        foreach (var s in slots ?? [])
        {
            var label = (s.Position ?? "").Trim().ToUpperInvariant();
            if (label is "TOTAL" or "STARTERS" or "*") { total ??= s.Max; continue; }
            var positions = label.Split('+', '/', ',')
                .Select(p => PlayerValuation.NormalizePosition(p))
                .Where(p => p.Length > 0)
                .ToHashSet();
            if (positions.Count == 0 || s.Max <= 0) continue;
            groups.Add(new SlotGroup(string.Join("+", positions), positions, Math.Max(0, s.Min), Math.Max(s.Min, s.Max)));
        }
        if (groups.Count == 0)
        {
            // Fallback: a standard 7-man lineup.
            groups = [G("QB", 1, 1), G("RB", 1, 2), G("WR", 2, 3), G("TE", 1, 2), G("K", 1, 1)];
        }
        var sumMin = groups.Sum(g => g.Min);
        var sumMax = groups.Sum(g => g.Max);
        var t = Math.Clamp(total ?? DefaultTotalStarters, sumMin, Math.Max(sumMin, sumMax));
        return new LineupRules { Groups = groups, TotalStarters = t };

        static SlotGroup G(string p, int min, int max) => new(p, new HashSet<string> { p }, min, max);
    }

    public SlotGroup? GroupFor(string pos) => Groups.FirstOrDefault(g => g.Positions.Contains(pos));

    public IEnumerable<string> Positions => Groups.SelectMany(g => g.Positions).Distinct();
}

public sealed record Lineup(IReadOnlyList<RatedPlayer> Starters, double Points)
{
    public bool Contains(string id) => Starters.Any(s => s.Id == id);
}

public static class LineupOptimizer
{
    // Greedy is optimal here: fill every group's minimum with its best players, then give the
    // remaining starter spots to the best players left whose group still has room.
    public static Lineup Best(IEnumerable<RatedPlayer> roster, LineupRules rules)
    {
        var available = roster
            .Where(r => rules.GroupFor(r.Pos) is not null)
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Value)
            .ToList();
        var starters = new List<RatedPlayer>();
        var used = new HashSet<string>();
        var counts = rules.Groups.ToDictionary(g => g, _ => 0);

        foreach (var g in rules.Groups)
        {
            foreach (var r in available.Where(r => g.Positions.Contains(r.Pos) && !used.Contains(r.Id)).Take(g.Min))
            {
                starters.Add(r); used.Add(r.Id); counts[g]++;
            }
        }
        foreach (var r in available)
        {
            if (starters.Count >= rules.TotalStarters) break;
            if (used.Contains(r.Id)) continue;
            var g = rules.GroupFor(r.Pos)!;
            if (counts[g] >= g.Max) continue;
            starters.Add(r); used.Add(r.Id); counts[g]++;
        }
        return new Lineup(starters, Math.Round(starters.Sum(s => s.Score), 2));
    }

    public static double Points(IEnumerable<RatedPlayer> roster, LineupRules rules) => Best(roster, rules).Points;
}
