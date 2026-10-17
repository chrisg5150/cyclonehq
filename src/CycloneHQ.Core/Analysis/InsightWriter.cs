using System.Globalization;
using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// Turns the numbers into plain-English advice for every player on my roster.
public static class InsightWriter
{
    public static PlayerInsight Write(LeagueContext ctx, string myId, RatedPlayer r, IReadOnlyList<string> flags, List<TradeIdea> assignedIdeas)
    {
        var me = ctx.Profiles[myId];
        var starting = me.OptimalStarterIds.Contains(r.Id);
        var ideas = assignedIdeas;
        var replacements = ctx.FreeAgents
            .Where(fa => fa.Pos == r.Pos && fa.Score > r.Score + 0.5 && !PlayerValuation.IsInjured(fa.Player.Injury))
            .OrderByDescending(fa => fa.Score)
            .Take(3)
            .ToList();
        var bestFa = replacements.FirstOrDefault();
        var first = FirstName(r.Name);
        var has = (string f) => flags.Contains(f);
        var goodIdea = ideas.FirstOrDefault(i => i.FitScore >= 55);
        var needText = goodIdea is not null
            ? goodIdea.Get.Select(id => ctx.Players[id]).OrderByDescending(p => p.Score).First().Pos
            : me.Needs.FirstOrDefault(n => n != r.Pos);

        string verdict, summary;
        if (starting && has(PlayerFlags.SellHigh) && goodIdea is not null)
        {
            verdict = "Sell";
            summary = $"{first} is scoring well above what his usage supports, so his price may never be higher. " +
                      $"Shop him for a long-term upgrade{(needText is null ? "" : $" at {needText}")}.";
        }
        else if (starting && (has(PlayerFlags.OnBye) || (r.Player.Injury?.Status ?? "").Trim().StartsWith("O", StringComparison.OrdinalIgnoreCase)))
        {
            verdict = "Bench";
            summary = has(PlayerFlags.OnBye)
                ? $"{first} is on bye this week, so sit him; he's back in your best lineup after that."
                : $"{first} is out this week, so sit him; he's in your best lineup when healthy.";
        }
        else if (starting)
        {
            verdict = "Start";
            summary = $"{first} is in your best lineup at {r.Pos}, projecting about {r.Score:0.0} points a week." +
                      (has(PlayerFlags.Underperforming) && bestFa is not null
                          ? $" He's been underwhelming, though, and {bestFa.Name} is sitting on waivers."
                          : has(PlayerFlags.Underperforming) ? " He's been underwhelming for a starter, so keep an eye on upgrades."
                          : has(PlayerFlags.BuyLow) ? " The usage is better than the box scores, so better weeks should come."
                          : has(PlayerFlags.Breakout) ? " He's trending up hard over the last three weeks."
                          : "");
        }
        else if (bestFa is not null && bestFa.Score > r.Score + 1 && r.Value < 30 && !has(PlayerFlags.BuyLow))
        {
            verdict = "Drop";
            summary = $"{first} isn't helping your lineup, and {bestFa.Name} on waivers projects {bestFa.Score - r.Score:0.0} points a week better.";
        }
        else if (has(PlayerFlags.BuyLow) || (has(PlayerFlags.Injured) && r.Value >= 30))
        {
            verdict = "Hold";
            summary = has(PlayerFlags.Injured)
                ? $"{first} is hurt, but he has real value when healthy, so stash him rather than sell for pennies."
                : r.Player.Usage is not null
                    ? $"{first}'s usage says better results are coming; don't sell him at his low point."
                    : $"Experts still rank {first} well above what he's scored so far; don't sell him at his low point.";
        }
        else if (goodIdea is not null && (me.Surplus.Contains(r.Pos) || r.Value >= 25))
        {
            verdict = "Sell";
            summary = $"{first} is good depth that other teams would start. Turn him into a lineup upgrade" +
                      $"{(needText is null ? "" : $" at {needText}")} instead of letting him sit.";
        }
        else
        {
            verdict = "Bench";
            summary = $"{first} is depth behind your starters at {r.Pos}; keep him for byes and injuries.";
        }

        return new PlayerInsight(r.Id, verdict, summary, Reasons(ctx, r, flags, starting), ideas,
            replacements.Select(x => x.Id).ToList());
    }

    static List<string> Reasons(LeagueContext ctx, RatedPlayer r, IReadOnlyList<string> flags, bool starting)
    {
        var p = r.Player;
        var st = p.Stats;
        var list = new List<string>();
        if (st is not null && st.GamesPlayed > 0)
            list.Add($"{st.PointsPerGame:0.0} pts/game over {st.GamesPlayed} games; {st.Last3PointsPerGame:0.0} over his last 3.");
        else
            list.Add("No games played yet this season.");
        if (st?.ProjectedNextWeek is double proj) list.Add($"Projected for {proj:0.0} points this week.");

        var ranks = new List<string>();
        if (p.Rank?.PositionRankLeague is int lr) ranks.Add($"{r.Pos}{lr} in this league's scoring");
        if (p.Rank?.ConsensusPositionRank is int cr) ranks.Add($"{r.Pos}{cr} in expert consensus rankings");
        if (ranks.Count > 0) list.Add($"Ranked {string.Join(" and ", ranks)}.");
        list.Add($"Trade value {r.Value:0}/100{(starting ? ", and he's in your best lineup" : ", currently on your bench")}.");

        var u = p.Usage;
        if (u is not null)
        {
            var parts = new List<string>();
            if (u.SnapPct is double s) parts.Add($"{Pct(s)} of snaps");
            if (u.TargetShare is double t && r.Pos is "WR" or "TE" or "RB") parts.Add($"{Pct(t)} of targets");
            if (u.RushShare is double rs && r.Pos is "RB" or "QB") parts.Add($"{Pct(rs)} of carries");
            if (u.RedZoneTouches is int rz) parts.Add($"{rz} red-zone touches");
            if (parts.Count > 0) list.Add($"Usage: {string.Join(", ", parts)}.");
        }

        var (xp, actual) = PlayerFlagger.ExpectedVsActual(p);
        foreach (var f in flags)
        {
            list.Add(f switch
            {
                PlayerFlags.Underperforming => "Underperforming: scoring well below what a starter at his spot usually gives.",
                PlayerFlags.Underutilized => u is not null
                    ? "Underutilized: his role is smaller than his talent and value suggest."
                    : "Underutilized: he carries real trade value but isn't in his team's best lineup.",
                PlayerFlags.BuyLow => xp is double x1 ? $"Buy-low profile: usage says {x1:0.0} pts/game, he's scored {actual:0.0}." : "Buy-low profile: experts rank him well above his production.",
                PlayerFlags.SellHigh => xp is double x2 ? $"Sell-high profile: scoring {actual:0.0} pts/game on usage worth about {x2:0.0}." : "Sell-high profile: producing well above where experts rank him.",
                PlayerFlags.Breakout => "Breakout: his last three games are well above his early-season pace.",
                PlayerFlags.Injured => $"Injury: {p.Injury?.Status}{(string.IsNullOrWhiteSpace(p.Injury?.Details) ? "" : $" ({p.Injury!.Details})")}.",
                PlayerFlags.OnBye => "On bye this week.",
                _ => f,
            });
        }
        return list;
    }

    public static (string headline, List<PositionNeed> needs) Headline(LeagueContext ctx, string myId, List<TradeIdea> ideas)
    {
        var me = ctx.Profiles[myId];
        var n = ctx.Profiles.Count;
        var f = ctx.League.Franchises.FirstOrDefault(x => x.Id == myId);
        var lineupRank = 1 + ctx.Profiles.Values.Count(p => p.ProjectedStarterPoints > me.ProjectedStarterPoints + 1e-9);

        // Needs are the lineup slots that trail the league median, worst first (TE shows up here through the
        // shared WR/TE and flex slots rather than on its own).
        var weak = ctx.WeakSlots(myId);
        var holeLabel = weak.Count > 0 && weak[0].Rank > n * 2 / 3 ? "Biggest hole" : "Weakest spot";
        var needs = weak.Select(s =>
        {
            var current = ctx.Players[s.PlayerId];
            var who = s.Positions.Count > 1
                ? string.Join(", ", s.Positions.Where(p => !PlayerValuation.NonTradePositions.Contains(p)).OrderBy(p => p))
                : s.Slot;
            var fa = ctx.FreeAgents.Where(x => s.Positions.Contains(x.Pos) && !PlayerValuation.IsInjured(x.Player.Injury))
                .OrderByDescending(x => x.Score).FirstOrDefault();
            var note = $"Your {TradeFinder.SlotName(s.Slot)} ({current.Name}, {s.Score:0.0} pts/game) ranks {Ordinal(s.Rank)} of {n}, " +
                       $"{-s.Gap:0.0} below the median team. " +
                       (s.Positions.Count > 1 ? $"Any {who} who beats him helps." : $"Look for a better {who}.") +
                       (fa is not null && fa.Score > s.Score ? $" Best free agent: {fa.Name} ({fa.Score:0.0})." : "");
            var chip = s.Positions.Count > 1 ? current.Pos : s.Positions.First();
            return new PositionNeed(chip, s.Rank, Math.Round(s.Gap, 2), note);
        }).ToList();

        var team = f?.Name ?? "Your team";
        var record = f is null ? "" : $" ({f.Wins}-{f.Losses}{(f.Ties > 0 ? $"-{f.Ties}" : "")})";
        var head = $"{team}{record} field the {Ordinal(lineupRank)}-best lineup of {n}, about {me.ProjectedStarterPoints:0} pts a week.";
        if (weak.Count > 0) head += $" {holeLabel}: {TradeFinder.SlotName(weak[0].Slot)} ({Ordinal(weak[0].Rank)} of {n}).";
        var best = ideas.FirstOrDefault();
        if (best is not null)
        {
            string Names(IEnumerable<string> ids) => string.Join(" + ", ids.Select(id => ctx.Players[id].Name));
            head += $" Top move: {Names(best.Give)} for {Names(best.Get)} (+{best.MyLineupGainPpg:0.0}/wk).";
        }
        return (head, needs);
    }

    static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    static string Pct(double v) => (v > 1 ? v / 100 : v).ToString("P0", CultureInfo.InvariantCulture).Replace(" ", "");

    public static string Ordinal(int n) => n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
