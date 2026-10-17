using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CycloneHQ.Sync.Mfl;

// Scores a stat line with a league's MFL rules, so expected stats become expected points in
// that league's scoring. Rules are per game: "*6" = 6 per unit; a plain number is a flat bonus
// when the stat falls inside the rule's range (e.g. 1 point for 250-299 passing yards).
sealed partial class MflScoringRules(Dictionary<string, List<MflScoringRules.Rule>> byEvent)
{
    public sealed record Rule(double Low, double High, double Points, bool PerUnit);

    public static MflScoringRules For(JsonElement? rules, string position)
    {
        var group = rules.List("rules", "positionRules")
            .FirstOrDefault(r => r.Str("positions").Split('|').Contains(position == "K" ? "PK" : position));
        var parsed = new Dictionary<string, List<Rule>>();
        if (group.ValueKind != JsonValueKind.Object) return new(parsed);

        foreach (var r in group.List("rule"))
        {
            var range = RangePattern().Match(r.Str("range"));
            var points = r.Str("points");
            var perUnit = points.StartsWith('*');
            if (!range.Success || !double.TryParse(perUnit ? points[1..] : points, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts))
                continue;
            var rule = new Rule(Parse(range.Groups[1].Value), Parse(range.Groups[2].Value), pts, perUnit);
            (parsed.TryGetValue(r.Str("event"), out var list) ? list : parsed[r.Str("event")] = []).Add(rule);
        }
        return new(parsed);
    }

    public double Score(Dictionary<string, double> stats)
    {
        double total = 0;
        foreach (var (evt, value) in stats)
        {
            if (!byEvent.TryGetValue(evt, out var rules)) continue;
            var rounded = Math.Round(value);   // tiers are whole numbers; expected stats are fractional
            foreach (var rule in rules)
            {
                if (rule.PerUnit) total += rule.Points * value;
                else if (rounded >= rule.Low && rounded <= rule.High) total += rule.Points;
            }
        }
        return total;
    }

    static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)$")]
    private static partial Regex RangePattern();
}
