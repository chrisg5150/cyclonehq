using CycloneHQ.Core.Analysis;
using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Tests;

public class LineupTests
{
    [Fact]
    public void Best_lineup_respects_combined_flex_slot_and_seven_starters()
    {
        var t = new TestLeague();
        var qb = t.Add("A", "QB", 20, 60);
        var qb2 = t.Add("A", "QB", 19, 50);   // better than any flex option, but only one QB may start
        var rb1 = t.Add("A", "RB", 16, 60);
        var rb2 = t.Add("A", "RB", 14, 50);
        var rb3 = t.Add("A", "RB", 13, 45);   // RB max is 2
        var wr1 = t.Add("A", "WR", 15, 55);
        var wr2 = t.Add("A", "WR", 12, 40);
        var te = t.Add("A", "TE", 11, 35);
        t.Add("A", "WR", 6, 10);
        var k = t.Add("A", "PK", 8, 3);

        var ctx = LeagueContext.Build(t.League(), t.Pool());
        var ids = ctx.Lineups["A"].Starters.Select(s => s.Id).ToList();

        Assert.Equal(7, ids.Count);
        Assert.Equal(new[] { qb, rb1, rb2, wr1, wr2, te, k }.Order(), ids.Order());
        Assert.DoesNotContain(qb2, ids);
        Assert.DoesNotContain(rb3, ids);
        Assert.Equal(20 + 16 + 14 + 15 + 12 + 11 + 8, ctx.Lineups["A"].Points, 1);
    }

    [Fact]
    public void Missing_inputs_degrade_gracefully()
    {
        var bare = new Player("1", "No Stats", "WR", "FA", null, null, null, null, null,
            new PlayerStats(0, 0, 0, 0, null, []), null, new PlayerRank(null, null, 0, "none"), []);
        Assert.Equal(0, PlayerValuation.BlendScore(bare));

        var projectionOnly = bare with { Stats = new PlayerStats(0, 0, 0, 0, 12.5, []) };
        Assert.Equal(12.5, PlayerValuation.BlendScore(projectionOnly), 2);

        // A league with no lineup rules still produces a 7-man lineup and a full analysis.
        var league = new LeagueInfo("x", "x", 2026, 1, [], new ScoringSummary(1, 4, ""),
            [new Franchise("A", "A", null, null, 0, 0, 0, 0, 0, 1, ["1"])], DateTimeOffset.UtcNow);
        var result = LeagueAnalyzer.Run(league, new PlayerPool(DateTimeOffset.UtcNow, [bare with { FantasyOwnerId = "A" }]), "A");
        Assert.Single(result.Analysis.Insights);
    }
}

public class ProfileTests
{
    [Fact]
    public void Weak_position_is_a_need_and_extra_starter_quality_depth_is_surplus()
    {
        var t = new TestLeague();
        // Team A: awful TE, but a third starting-quality RB on the bench.
        t.Add("A", "QB", 18, 50);
        t.Add("A", "RB", 13, 45);
        t.Add("A", "RB", 12, 40);
        t.Add("A", "RB", 12, 40);
        t.Add("A", "WR", 13, 45);
        t.Add("A", "WR", 11, 35);
        t.Add("A", "WR", 8, 20);
        t.Add("A", "TE", 2, 2);
        t.Add("A", "PK", 8, 3);
        foreach (var name in new[] { "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" }) t.AddFillerTeam(name);

        // With a dedicated TE slot, the awful TE is a need.
        var result = LeagueAnalyzer.Run(t.League(TestLeague.StandardLineup), t.Pool(), "A");
        var a = result.Profiles.Teams.Single(p => p.FranchiseId == "A");

        Assert.Equal(12, result.Profiles.Teams.Count);
        Assert.Contains("TE", a.Needs);
        Assert.Contains("RB", a.Surplus);
        Assert.DoesNotContain("RB", a.Needs);
        var te = a.Positions.Single(p => p.Position == "TE");
        Assert.Equal(12, te.LeagueRank);
        Assert.True(te.VsLeagueMedian < 0);

        // In this league's shared WR+TE slot, a team whose receivers carry the group doesn't need a TE.
        var flex = LeagueAnalyzer.Run(t.League(), t.Pool(), "A").Profiles.Teams.Single(p => p.FranchiseId == "A");
        Assert.DoesNotContain("TE", flex.Needs);
    }
}

public class TradeTests
{
    [Fact]
    public void Each_target_appears_in_at_most_two_ideas_and_lists_spread_across_partners()
    {
        static TradeIdea Idea(string partner, string give, string get, double fit) =>
            new(partner, [give], [get], 50, 50, 2, 0, fit, "x");
        // Five of my players all have their best idea for the same star, plus a lesser option each.
        var ranked = new List<TradeIdea>();
        foreach (var p in new[] { "m1", "m2", "m3", "m4", "m5" })
        {
            ranked.Add(Idea("B", p, "star", 90));
            ranked.Add(Idea("B", p, "starB2", 85));
            ranked.Add(Idea("C", p, $"other-{p}", 60));
        }
        ranked = ranked.OrderByDescending(i => i.FitScore).ToList();

        var lists = TradeFinder.Assign(ranked, ["m1", "m2", "m3", "m4", "m5"]);
        var shown = lists.Values.SelectMany(l => l).Distinct().ToList();

        Assert.Equal(2, shown.Count(i => i.Get.Contains("star")));
        Assert.All(lists.Values, l => Assert.Equal(l.Count, l.Select(i => i.PartnerFranchiseId).Distinct().Count()));
        Assert.All(lists.Values, l => Assert.NotEmpty(l));
    }

    [Fact]
    public void Tiny_gains_and_qb_sideways_swaps_are_not_proposed()
    {
        var t = new TestLeague();
        var myQb = t.Add("A", "QB", 18, 50);
        t.AddFillerTeam("A");
        var theirQb = t.Add("B", "QB", 18.5, 52);   // +0.5 a week: not worth a trade
        t.AddFillerTeam("B");
        foreach (var name in new[] { "C", "D", "E" }) t.AddFillerTeam(name);

        var ctx = LeagueContext.Build(t.League(), t.Pool());
        var all = TradeFinder.FindAll(ctx, "A");
        Assert.DoesNotContain(all, i => i.MyLineupGainPpg < TradeFinder.MinNeedGain);
        Assert.DoesNotContain(all, i => i.Give.Contains(myQb) && i.Get.Contains(theirQb));
    }

    [Fact]
    public void Win_win_trade_ranks_above_a_lopsided_one()
    {
        var t = new TestLeague();
        // Me (A): deep at RB, weak at WR.
        t.Add("A", "QB", 18, 50);
        t.Add("A", "RB", 15, 55);
        var myRb2 = t.Add("A", "RB", 14, 50, "Depth Back");
        var myRb3 = t.Add("A", "RB", 13, 48, "Third Back");
        t.Add("A", "WR", 9, 20);
        t.Add("A", "WR", 7, 12);
        t.Add("A", "WR", 6, 10);
        t.Add("A", "TE", 8, 20);
        t.Add("A", "PK", 8, 3);

        // B: deep at WR, thin at RB. A WR for one of my RBs helps both of us.
        t.Add("B", "QB", 18, 50);
        t.Add("B", "RB", 13, 45);
        t.Add("B", "RB", 4, 4);
        t.Add("B", "WR", 15, 55);
        t.Add("B", "WR", 14, 50);
        t.Add("B", "WR", 13, 48);
        var bWr4 = t.Add("B", "WR", 13, 48, "Spare Receiver");
        t.Add("B", "TE", 8, 20);
        t.Add("B", "PK", 8, 3);

        // C: has a star WR they would never give up for my third RB.
        t.Add("C", "QB", 18, 50);
        t.Add("C", "RB", 13, 45);
        t.Add("C", "RB", 9, 25);
        var cStar = t.Add("C", "WR", 24, 95, "Star Receiver");
        t.Add("C", "WR", 11, 35);
        t.Add("C", "WR", 8, 20);
        t.Add("C", "TE", 8, 20);
        t.Add("C", "PK", 8, 3);
        foreach (var name in new[] { "D", "E", "F", "G" }) t.AddFillerTeam(name);

        var ctx = LeagueContext.Build(t.League(), t.Pool());
        var all = TradeFinder.FindAll(ctx, "A");

        var winWin = all.First(i => i.PartnerFranchiseId == "B" && i.Get.SequenceEqual([bWr4])
                                    && i.Give.Count == 1 && (i.Give[0] == myRb2 || i.Give[0] == myRb3));
        Assert.True(winWin.MyLineupGainPpg > 0);
        Assert.True(winWin.PartnerLineupGainPpg > 0);
        Assert.False(string.IsNullOrWhiteSpace(winWin.Rationale));

        // The lopsided deal (my third RB for their star) is rejected outright...
        var p = ctx.Players;
        var lopsided = TradeFinder.Evaluate(ctx, "A", "C", ctx.Rosters["A"], ctx.Rosters["C"],
            ctx.Lineups["A"].Points, ctx.Lineups["C"].Points, [p[myRb3]], [p[cStar]]);
        Assert.Null(lopsided);
        Assert.DoesNotContain(all, i => i.Get.Contains(cStar) && i.Give.SequenceEqual([myRb3]));

        // ...and even scored on its merits, a huge gain for me that guts the partner loses to the win-win.
        var gapStarDeal = (p[cStar].Value - p[myRb3].Value) / p[cStar].Value;
        var lopsidedScore = TradeFinder.FitScore(myGain: 7, theirGain: -5, valueGap: gapStarDeal, needFit: 0.6);
        Assert.True(winWin.FitScore > lopsidedScore);

        // The idea also shows up on that RB's insight, and the headline names WR as the hole.
        var result = LeagueAnalyzer.Run(t.League(), t.Pool(), "A");
        var insight = result.Analysis.Insights.Single(i => i.PlayerId == winWin.Give[0]);
        Assert.Contains(insight.TradeIdeas, i => i.PartnerFranchiseId == "B");
        Assert.Contains(result.Analysis.Needs, n => n.Position == "WR");
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis.Headline));
    }
}
