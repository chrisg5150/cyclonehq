using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

public static class PlayerFlagger
{
    public static List<string> Flags(LeagueContext ctx, RatedPlayer r)
    {
        var flags = new List<string>();
        var p = r.Player;
        var st = p.Stats;
        var gp = st?.GamesPlayed ?? 0;
        var bar = ctx.StarterBar.GetValueOrDefault(r.Pos);
        var owner = ctx.OwnerOf(r.Id);
        var starting = owner is not null && ctx.Lineups.TryGetValue(owner, out var lu) && lu.Contains(r.Id);

        if (PlayerValuation.IsInjured(p.Injury)) flags.Add(PlayerFlags.Injured);
        if (p.ByeWeek is int bye && bye == ctx.League.CurrentWeek) flags.Add(PlayerFlags.OnBye);

        if (st is not null && gp >= 2 && !PlayerValuation.NonTradePositions.Contains(r.Pos))
        {
            var breakout = IsBreakout(r, st, bar);
            if (!breakout && IsUnderperforming(ctx, r, starting, bar)) flags.Add(PlayerFlags.Underperforming);
            if (IsUnderutilized(ctx, r, owner, starting)) flags.Add(PlayerFlags.Underutilized);
            var (expected, actual) = ExpectedVsActual(p);
            if (expected is double xp)
            {
                if (xp - actual >= Math.Max(2, 0.2 * xp)) flags.Add(PlayerFlags.BuyLow);
                else if (actual - xp >= Math.Max(3, 0.25 * xp)) flags.Add(PlayerFlags.SellHigh);
            }
            else if (p.Rank?.PositionRankLeague is int lr && p.Rank.ConsensusPositionRank is int cr && gp >= 3)
            {
                if (cr <= 24 && lr >= cr + Math.Max(10, cr)) flags.Add(PlayerFlags.BuyLow);
                else if (lr <= 24 && cr >= lr + Math.Max(10, lr)) flags.Add(PlayerFlags.SellHigh);
            }
            if (breakout) flags.Add(PlayerFlags.Breakout);
        }
        return flags;
    }

    // Starting but scoring well below what a starter at his slot normally gives, or well below what
    // players at his expert rank are scoring in this league.
    static bool IsUnderperforming(LeagueContext ctx, RatedPlayer r, bool starting, double bar)
    {
        var st = r.Player.Stats;
        var form = (st.PointsPerGame + st.Last3PointsPerGame) / 2;
        if (starting && bar > 0 && form < 0.75 * bar) return true;
        if (st.GamesPlayed >= 3 && r.Player.Rank?.ConsensusPositionRank is int cr && cr <= 36)
        {
            var atRank = ctx.Players.Values
                .Where(x => x.Pos == r.Pos && (x.Player.Stats?.GamesPlayed ?? 0) >= 2)
                .Select(x => x.Player.Stats.PointsPerGame)
                .OrderByDescending(x => x)
                .Skip(cr - 1)
                .FirstOrDefault();
            return atRank > 0 && form < 0.7 * atRank;
        }
        return false;
    }

    // Talent/value says he should be a big part of the offense, but snaps or touches are low;
    // or he's rostered with real value but stuck on his fantasy bench.
    static bool IsUnderutilized(LeagueContext ctx, RatedPlayer r, string? owner, bool starting)
    {
        var peers = ctx.Players.Values.Where(x => x.Pos == r.Pos).Select(x => x.Value).OrderByDescending(v => v).ToList();
        var cutoff = peers.Count >= 5 ? peers[(int)(peers.Count * 0.3)] : 40;
        if (r.Value < Math.Max(cutoff, 25)) return false;
        var u = r.Player.Usage;
        if (u is null)
            return owner is not null && !starting && !PlayerValuation.IsInjured(r.Player.Injury)
                   && r.Player.ByeWeek != ctx.League.CurrentWeek;
        var snap = Frac(u.SnapPct);
        return r.Pos switch
        {
            "RB" => (Frac(u.RushShare) is double rs && rs < 0.4) || (snap is double s1 && s1 < 0.45),
            "WR" => (Frac(u.TargetShare) is double ts && ts < 0.16) || (snap is double s2 && s2 < 0.6),
            "TE" => (Frac(u.TargetShare) is double tt && tt < 0.12) || (snap is double s3 && s3 < 0.55),
            "QB" => snap is double s4 && s4 < 0.8,
            _ => false,
        };
    }

    // Last three games well above his earlier pace and now at starter level. Thresholds scale with the
    // league's scoring (the starter bar), and established stars don't count as breakouts.
    static bool IsBreakout(RatedPlayer r, PlayerStats st, double bar)
    {
        if (st.GamesPlayed < 5 || bar <= 0 || r.Value >= 75) return false;
        var earlier = (st.PointsPerGame * st.GamesPlayed - st.Last3PointsPerGame * 3) / (st.GamesPlayed - 3);
        var bigJump = st.Last3PointsPerGame - earlier >= Math.Max(2.5, 0.75 * bar)
                      && st.Last3PointsPerGame >= 1.5 * Math.Max(earlier, 0.5);
        return bigJump && st.Last3PointsPerGame >= 1.2 * bar;
    }

    // Usage-based expected points per game vs actual. Falls back to the weekly expected column.
    public static (double? expected, double actual) ExpectedVsActual(Player p)
    {
        var actual = p.Stats?.PointsPerGame ?? 0;
        var u = p.Usage;
        if (u?.ExpectedPointsPerGame is double xp && xp > 0) return (xp, actual);
        if (u?.PointsOverExpectedPerGame is double poe) return (actual - poe, actual);
        var weeks = p.Stats?.Weekly?.Where(w => w.Expected is not null).ToList() ?? [];
        if (weeks.Count >= 2) return (weeks.Average(w => w.Expected!.Value), weeks.Average(w => w.Points));
        return (null, actual);
    }

    // Accepts both 0-1 and 0-100 percentages.
    static double? Frac(double? v) => v is double d ? (d > 1.0 ? d / 100.0 : d) : null;
}
