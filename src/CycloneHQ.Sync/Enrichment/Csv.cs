using System.Globalization;
using System.Text;

namespace CycloneHQ.Sync.Enrichment;

// A downloaded CSV file: header names plus rows, read by column name.
sealed class CsvTable(string[] header, List<string[]> rows)
{
    readonly Dictionary<string, int> _index = header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    public IReadOnlyList<string[]> Rows => rows;

    public string Get(string[] row, string column) =>
        _index.TryGetValue(column, out var i) && i < row.Length && row[i] is not ("NA" or "") ? row[i] : "";

    public double? Num(string[] row, string column) =>
        double.TryParse(Get(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static async Task<CsvTable?> LoadAsync(HttpClient http, string url, string label)
    {
        try
        {
            var table = Parse(await http.GetStringAsync(url));
            Log.Info($"{label}: {table.Rows.Count} rows");
            return table;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warn($"{label} unavailable: {ex.Message}");
            return null;
        }
    }

    // Minimal RFC 4180 reader: commas, quoted fields, doubled quotes inside quotes.
    static CsvTable Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                fields.Add(field.ToString().TrimEnd('\r')); field.Clear();
                rows.Add([.. fields]); fields.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add([.. fields]); }
        return new CsvTable(rows[0], rows.Skip(1).Where(r => r.Length > 1).ToList());
    }
}
