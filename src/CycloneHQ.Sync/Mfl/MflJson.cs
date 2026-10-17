using System.Globalization;
using System.Text.Json;

namespace CycloneHQ.Sync.Mfl;

// MFL's JSON has two quirks: every number is a string, and a list with one item
// comes back as a bare object. These helpers smooth both over.
static class MflJson
{
    public static IEnumerable<JsonElement> List(this JsonElement? root, params string[] path)
    {
        if (root.Get(path) is not { } e) yield break;
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray()) yield return item;
        else if (e.ValueKind == JsonValueKind.Object)
            yield return e;
    }

    public static JsonElement? Get(this JsonElement? root, params string[] path)
    {
        if (root is not { } e) return null;
        foreach (var name in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e)) return null;
        return e;
    }

    public static IEnumerable<JsonElement> List(this JsonElement e, params string[] path) => ((JsonElement?)e).List(path);

    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => StraightQuotes(v.GetString() ?? ""),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.Object when v.TryGetProperty("$t", out var t) => t.GetString() ?? "",
                _ => "",
            }
            : "";

    // "Matson’s Maulers" -> "Matson's Maulers": curly quotes render badly in some consoles and fonts.
    static string StraightQuotes(string s) =>
        s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');

    public static double Num(this JsonElement e, string name) =>
        double.TryParse(e.Str(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    public static int Int(this JsonElement e, string name) => (int)e.Num(name);

    // "13418,17501," -> ["13418", "17501"]
    public static string[] Ids(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
