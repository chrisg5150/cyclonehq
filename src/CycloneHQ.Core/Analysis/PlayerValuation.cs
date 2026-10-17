using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// A player plus the two numbers the engine works with:
// Score = expected fantasy points per game rest of season, Value = 0-100 trade value.
public sealed class RatedPlayer
{
    public required Player Player { get; init; }
    public required string Pos { get; init; }
    public double Score { get; set; }
    public double Value { get; set; }
    public string Id => Player.Id;
    public string Name => Player.Name;
    public override string ToString() => $"{Name} ({Pos}) {Score:0.0}ppg v{Value:0}";
}

public static class PlayerValuation
{
    // Positions that are never worth building a trade around.
    public static readonly HashSet<string> NonTradePositions = ["K", "DEF"];

    public static string NormalizePosition(string? pos) => (pos ?? "").Trim().ToUpperInvariant() switch
    {
        "PK" or "K" => "K",
        "DEF" or "DST" or "D/ST" or "DEFENSE" or "TMDEF" => "DEF",
        var p => p,
    };

    public static Dictionary<string, RatedPlayer> Rate(IEnumerable<Player> players)
    {
        var list = players.Select(p => new RatedPlayer { Player = p, Pos = NormalizePosition(p.Position) }).ToList();
        var fits = list.GroupBy(r => r.Pos).ToDictionary(g => g.Key, g => ValueFit.From(g));

        foreach (var r in list)
            r.Score = Math.Round(BlendScore(r.Player, fits[r.Pos]) * InjuryFactor(r.Player.Injury), 2);

        // Trade value: use the source value when there is one, otherwise estimate it from points.
        var maxByPos = list.GroupBy(r => r.Pos).ToDictionary(g => g.Key, g => Math.Max(1, g.Max(r => r.Score)));
        foreach (var r in list)
        {
            var tv = r.Player.Rank?.TradeValue ?? 0;
            if (tv > 0) { r.Value = tv; continue; }
            var fit = fits[r.Pos];
            r.Value = fit is not null
                ? Math.Clamp(fit.InverseValue(r.Score), 0, 100)
                : Math.Clamp(100 * Math.Pow(r.Score / maxByPos[r.Pos], 2) * PositionScale(r.Pos), 0, 100);
            r.Value = Math.Round(r.Value, 1);
        }
        return list.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
    }

    // Blend of season average, recent form, next-week projection, usage-based expectation and trade value.
    // Every input is optional; missing ones drop out of the weighted average.
    public static double BlendScore(Player p, ValueFit? fit = null)
    {
        double sum = 0, weight = 0;
        void Add(double v, double w) { if (w > 0) { sum += v * w; weight += w; } }

        var st = p.Stats;
        var gp = st?.GamesPlayed ?? 0;
        if (st is not null && gp > 0)
        {
            Add(st.PointsPerGame, 0.35 * Math.Min(1, gp / 4.0));
            if (st.Last3PointsPerGame > 0 || gp >= 3) Add(st.Last3PointsPerGame, 0.2 * Math.Min(1, gp / 3.0));
        }
        if (st?.ProjectedNextWeek is double proj && proj > 0) Add(proj, 0.25);
        // Expected points from usage (opportunity), either as a season figure or from the weekly column.
        if (p.Usage?.ExpectedPointsPerGame is double xp && xp > 0) Add(xp, 0.15);
        else if (st?.Weekly?.Where(w => w.Expected is not null).Select(w => w.Expected!.Value).ToList() is { Count: >= 2 } xs)
            Add(xs.Average(), 0.15);
        var tv = p.Rank?.TradeValue ?? 0;
        if (fit is not null && tv > 0) Add(fit.Predict(tv), gp == 0 ? 0.4 : 0.15);
        return weight > 0 ? sum / weight : 0;
    }

    public static double InjuryFactor(InjuryInfo? injury)
    {
        if (injury is null || string.IsNullOrWhiteSpace(injury.Status)) return 1;
        var s = injury.Status.Trim().ToUpperInvariant();
        if (s.Contains("IR") || s.Contains("RESERVE") || s.Contains("SEASON") || s.Contains("PUP") || s.Contains("SUSP")) return 0.5;
        if (s.StartsWith("O")) return 0.85;          // Out
        if (s.StartsWith("D")) return 0.92;          // Doubtful
        if (s.StartsWith("Q")) return 0.97;          // Questionable
        return 1;
    }

    public static bool IsInjured(InjuryInfo? injury) => InjuryFactor(injury) < 0.95;

    static double PositionScale(string pos) => pos switch
    {
        "QB" => 0.8, "RB" or "WR" => 1.0, "TE" => 0.85, _ => 0.3,
    };
}

// Per-position linear fit of points per game against trade value, so a value can stand in for missing points.
public sealed class ValueFit
{
    public double Intercept { get; init; }
    public double Slope { get; init; }
    public double Predict(double value) => Math.Max(0, Intercept + Slope * value);
    public double InverseValue(double ppg) => (ppg - Intercept) / Slope;

    public static ValueFit? From(IEnumerable<RatedPlayer> players)
    {
        var pts = players
            .Where(r => (r.Player.Stats?.GamesPlayed ?? 0) >= 2 && (r.Player.Rank?.TradeValue ?? 0) > 0)
            .Select(r => (x: r.Player.Rank.TradeValue, y: r.Player.Stats.PointsPerGame))
            .ToList();
        if (pts.Count < 6) return null;
        double mx = pts.Average(p => p.x), my = pts.Average(p => p.y);
        double sxx = pts.Sum(p => (p.x - mx) * (p.x - mx)), sxy = pts.Sum(p => (p.x - mx) * (p.y - my));
        if (sxx <= 0) return null;
        var slope = sxy / sxx;
        if (slope <= 0.01) return null;
        return new ValueFit { Intercept = my - slope * mx, Slope = slope };
    }
}
