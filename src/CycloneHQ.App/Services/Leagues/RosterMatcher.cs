using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CycloneHQ.Core.Models;

namespace CycloneHQ.App.Services.Leagues;

public enum MatchQuality { Exact, Loose, Missing }

// One name pulled from the paste, and what it matched in directory.json.
public sealed record RosterMatch(string RawName, string? Position, string? Team, DirectoryEntry? Entry, MatchQuality Quality)
{
    public IReadOnlyList<DirectoryEntry> Suggestions { get; init; } = [];
}

// Turns roster text copied from ESPN, Yahoo, Sleeper etc. (or a CSV) into directory players.
// Handles "Last, First", suffixes (Jr., III), D/ST lines, glued team codes ("Josh AllenBuf") and common nicknames.
public sealed partial class RosterMatcher
{
    static readonly HashSet<string> PositionTokens = new(StringComparer.OrdinalIgnoreCase)
        { "QB", "RB", "WR", "TE", "K", "PK", "DEF", "DST", "D/ST", "DL", "LB", "DB", "FLEX", "RB/WR", "WR/TE", "RB/WR/TE", "OP", "SF", "BN", "BE", "IR", "SUPERFLEX", "W/R/T", "Q/R/W/T" };

    static readonly HashSet<string> StatusTokens = new(StringComparer.OrdinalIgnoreCase)
        { "Q", "O", "D", "P", "IR", "PUP", "SUSP", "SSPD", "NA", "OUT", "DTD", "COV", "INJ", "FA", "BYE", "W", "L", "T", "Final" };

    // Opponent markers: whatever follows is the other NFL team, not part of the name.
    static readonly HashSet<string> OpponentTokens = new(StringComparer.OrdinalIgnoreCase) { "@", "vs", "vs.", "at", "v" };

    // Lines that are page furniture, not players.
    static readonly HashSet<string> Furniture = new(StringComparer.OrdinalIgnoreCase)
    {
        "starters", "bench", "slot", "player", "players", "opp", "status", "proj", "projected", "score", "total", "totals",
        "pts", "points", "fpts", "week", "season", "action", "actions", "move", "add", "drop", "trade", "waivers", "roster",
        "injured reserve", "reserve", "lineup", "stats", "news", "research", "fantasy", "rank", "rost", "start", "position",
    };

    static readonly HashSet<string> Suffixes = ["jr", "sr", "ii", "iii", "iv", "v"];

    static readonly Dictionary<string, string> Nicknames = new()
    {
        ["gabe"] = "gabriel", ["mike"] = "michael", ["chris"] = "christopher", ["matt"] = "matthew", ["josh"] = "joshua",
        ["rob"] = "robert", ["bob"] = "robert", ["robbie"] = "robert", ["will"] = "william", ["bill"] = "william",
        ["kenny"] = "kenneth", ["ken"] = "kenneth", ["jon"] = "jonathan", ["johnny"] = "john", ["nate"] = "nathan",
        ["nick"] = "nicholas", ["tony"] = "anthony", ["zach"] = "zachary", ["zack"] = "zachary", ["alex"] = "alexander",
        ["sam"] = "samuel", ["dan"] = "daniel", ["danny"] = "daniel", ["joe"] = "joseph", ["jake"] = "jacob",
        ["ben"] = "benjamin", ["tom"] = "thomas", ["tommy"] = "thomas", ["jim"] = "james", ["jimmy"] = "james",
        ["pat"] = "patrick", ["steve"] = "steven", ["stephen"] = "steven", ["drew"] = "andrew", ["andy"] = "andrew",
        ["greg"] = "gregory", ["jeff"] = "jeffrey", ["hollywood"] = "marquise", ["chig"] = "chigoziem",
        ["cam"] = "cameron", ["dave"] = "david", ["rich"] = "richard",
        ["ricky"] = "richard", ["fred"] = "frederick", ["ed"] = "edward", ["eddie"] = "edward", ["tim"] = "timothy",
        ["jerry"] = "jerome", ["mo"] = "mohamed", ["deebo"] = "tyshun", ["scotty"] = "scott", ["kenny"] = "kenneth",
    };

    // Defenses by city and nickname, keyed to ESPN-style codes (NflTeams.Code normalizes MFL's codes to the same).
    static readonly (string Code, string City, string Nick)[] TeamNames =
    [
        ("ARI", "arizona", "cardinals"), ("ATL", "atlanta", "falcons"), ("BAL", "baltimore", "ravens"), ("BUF", "buffalo", "bills"),
        ("CAR", "carolina", "panthers"), ("CHI", "chicago", "bears"), ("CIN", "cincinnati", "bengals"), ("CLE", "cleveland", "browns"),
        ("DAL", "dallas", "cowboys"), ("DEN", "denver", "broncos"), ("DET", "detroit", "lions"), ("GB", "green bay", "packers"),
        ("HOU", "houston", "texans"), ("IND", "indianapolis", "colts"), ("JAX", "jacksonville", "jaguars"), ("KC", "kansas city", "chiefs"),
        ("LV", "las vegas", "raiders"), ("LAC", "los angeles chargers", "chargers"), ("LAR", "los angeles rams", "rams"),
        ("MIA", "miami", "dolphins"), ("MIN", "minnesota", "vikings"), ("NE", "new england", "patriots"), ("NO", "new orleans", "saints"),
        ("NYG", "new york giants", "giants"), ("NYJ", "new york jets", "jets"), ("PHI", "philadelphia", "eagles"),
        ("PIT", "pittsburgh", "steelers"), ("SF", "san francisco", "49ers"), ("SEA", "seattle", "seahawks"), ("TB", "tampa bay", "buccaneers"),
        ("TEN", "tennessee", "titans"), ("WSH", "washington", "commanders"),
    ];

    static readonly HashSet<string> TeamCodes = new(
        TeamNames.Select(t => t.Code).Concat(["GBP", "KCC", "NEP", "NOS", "SFO", "TBB", "LVR", "JAC", "WAS", "LA"]),
        StringComparer.OrdinalIgnoreCase);

    readonly IReadOnlyList<DirectoryEntry> _all;
    readonly Dictionary<string, List<DirectoryEntry>> _byKey = new();
    readonly Dictionary<string, List<DirectoryEntry>> _byLast = new();
    readonly Dictionary<string, DirectoryEntry> _defByTeam = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<DirectoryEntry, string> _keyOf = new(ReferenceEqualityComparer.Instance);

    public RosterMatcher(PlayerDirectory directory)
    {
        _all = directory.Players;
        foreach (var e in _all)
        {
            if (IsDefense(e.Position))
            {
                _defByTeam.TryAdd(NflTeams.Code(e.NflTeam), e);
                continue;
            }
            var key = Key(e.Name);
            _keyOf[e] = key;
            Add(_byKey, key, e);
            var last = key.Split(' ').Last();
            Add(_byLast, last, e);
        }
    }

    public IReadOnlyList<DirectoryEntry> All => _all;

    public static bool IsDefense(string? pos) => pos is not null &&
        (pos.Equals("DEF", StringComparison.OrdinalIgnoreCase) || pos.Equals("DST", StringComparison.OrdinalIgnoreCase)
         || pos.Equals("D/ST", StringComparison.OrdinalIgnoreCase) || pos.Equals("Def", StringComparison.OrdinalIgnoreCase));

    // ---- parsing ----------------------------------------------------------------------------

    public IReadOnlyList<RosterMatch> MatchText(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var candidates = LooksLikeCsv(lines)
            ? ParseCsv(lines)
            : lines.SelectMany(SplitPieces).Select(ParseLine).OfType<(string, string?, string?)>().ToList();

        var seen = new HashSet<string>();
        var results = new List<RosterMatch>();
        foreach (var (name, pos, team) in candidates)
        {
            var m = Match(name, pos, team);
            var dedupe = m.Entry?.Id ?? "raw:" + Key(name);
            if (!seen.Add(dedupe)) continue;
            results.Add(m);
        }
        return results;
    }

    static bool LooksLikeCsv(List<string> lines)
    {
        if (lines.Count == 0) return false;
        var header = lines[0].ToLowerInvariant();
        if ((header.Contains(',') || header.Contains('\t')) && (header.Contains("player") || header.Contains("name"))) return true;
        // Without a header, only call it a CSV when several rows share the same column count;
        // a single "Bijan Robinson, Ja'Marr Chase, ..." line is a list of names, not a table.
        if (lines.Count < 2) return false;
        var widths = lines.Where(l => l.Contains(',')).Select(l => SplitCsv(l, ',').Count).Where(n => n >= 3).ToList();
        return widths.Count > lines.Count * 0.6 && widths.GroupBy(n => n).Max(g => g.Count()) > widths.Count * 0.8;
    }

    // One pasted line can hold several names: "Bijan Robinson, Ja'Marr Chase; Travis Kelce".
    // A single "Allen, Josh" (or "Josh Allen, QB") stays whole so ParseLine can read it.
    static IEnumerable<string> SplitPieces(string line)
    {
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var commas = part.Split(',', StringSplitOptions.TrimEntries);
            if (commas.Length == 2 && !commas[1].Contains(' ')) { yield return part; continue; }
            foreach (var c in commas.Where(c => c.Length > 0)) yield return c;
        }
    }

    List<(string, string?, string?)> ParseCsv(List<string> lines)
    {
        var sep = lines[0].Contains('\t') && !lines[0].Contains(',') ? '\t' : ',';
        var header = SplitCsv(lines[0], sep).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Any(n => h == n || h.Contains(n)));

        var hasHeader = header.Any(h => h.Contains("name") || h.Contains("player") || h is "pos" or "position" or "team");
        int nameCol = hasHeader ? Col("player", "name") : 0;
        int firstCol = hasHeader ? header.FindIndex(h => h.StartsWith("first")) : -1;
        int lastCol = hasHeader ? header.FindIndex(h => h.StartsWith("last")) : -1;
        int posCol = hasHeader ? Col("pos", "position") : -1;
        int teamCol = hasHeader ? header.FindIndex(h => h is "team" or "nfl team" or "nfl" or "tm") : -1;

        var output = new List<(string, string?, string?)>();
        foreach (var line in lines.Skip(hasHeader ? 1 : 0))
        {
            var cells = SplitCsv(line, sep);
            string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : "";

            var name = firstCol >= 0 && lastCol >= 0 ? $"{Cell(firstCol)} {Cell(lastCol)}" : Cell(Math.Max(nameCol, 0));
            if (string.IsNullOrWhiteSpace(name)) continue;
            var parsed = ParseLine(name);
            if (parsed is null) continue;
            var (n, p, t) = parsed.Value;
            output.Add((n, NullIfEmpty(Cell(posCol)) ?? p, NullIfEmpty(Cell(teamCol)) ?? t));
        }
        return output;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static List<string> SplitCsv(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (c == sep && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    // Pulls "name, position hint, team hint" out of one pasted line, or null for page furniture.
    (string Name, string? Pos, string? Team)? ParseLine(string line)
    {
        var s = line.Replace(' ', ' ').Trim();
        if (s.Length < 3 || s.Length > 80) return null;
        if (Furniture.Contains(s.Trim(':'))) return null;

        string? pos = null, team = null;
        var isDef = Regex.IsMatch(s, @"\b(d/st|dst|defense|def)\b", RegexOptions.IgnoreCase);

        // "Josh AllenBuf QB" -> "Josh Allen Buf QB"
        s = GluedTeam().Replace(s, m => TeamCodes.Contains(m.Groups[2].Value) ? m.Groups[1].Value + " " + m.Groups[2].Value : m.Value);
        s = Regex.Replace(s, @"[•·|()\[\]]", " ");

        // "Allen, Josh" -> "Josh Allen"
        var comma = s.Split(',');
        if (comma.Length == 2 && IsHint(comma[1].Trim()))
            s = comma[0].Trim() + " " + comma[1].Trim();
        else if (comma.Length == 2 && comma[0].Trim().Length > 1 && comma[1].Trim().Length > 1 && !comma[1].Trim().Contains(' '))
            s = comma[1].Trim() + " " + comma[0].Trim();
        else if (comma.Length >= 2)
            s = comma[0];

        var kept = new List<string>();
        foreach (var raw in s.Split([' ', '\t', '-', '–', '—'], StringSplitOptions.RemoveEmptyEntries))
        {
            var tok = raw.Trim(',', ';', ':', '*');
            if (tok.Length == 0) continue;
            if (OpponentTokens.Contains(tok) || tok.StartsWith('@')) { if (kept.Count >= 2) break; return null; }
            if (PositionTokens.Contains(tok)) { pos ??= tok.ToUpperInvariant(); continue; }
            // Team codes are only stripped when they trail a name, so "Will Levis" keeps his first name.
            if (kept.Count >= 2 && TeamCodes.Contains(tok)) { team ??= tok.ToUpperInvariant(); continue; }
            if (StatusTokens.Contains(tok) && kept.Count >= 2) continue;
            if (tok.Any(char.IsDigit)) { if (kept.Count >= 2) break; continue; }
            kept.Add(tok);
        }
        if (isDef)
        {
            var code = DefenseCode(string.Join(' ', kept));
            if (code is not null) return ($"{code} D/ST", "DEF", code);
        }
        // A bare team name like "Bears" or "Chicago Bears" is a defense too.
        if (kept.Count is 1 or 2 or 3 && DefenseCode(string.Join(' ', kept), exact: true) is { } teamOnly)
            return ($"{teamOnly} D/ST", "DEF", teamOnly);

        if (kept.Count < 2 || kept.Count > 5 || kept.All(k => Furniture.Contains(k))) return null;
        var name = string.Join(' ', kept);
        if (Furniture.Contains(name) || !name.Any(char.IsLetter)) return null;
        return (name, pos, team);
    }

    // "QB", "BUF", "Q": a position, team or status after a comma, not a first name.
    static bool IsHint(string tok) => PositionTokens.Contains(tok) || TeamCodes.Contains(tok) || StatusTokens.Contains(tok);

    static string? DefenseCode(string text, bool exact = false)
    {
        var t = Fold(text).ToLowerInvariant().Replace("d/st", "").Replace("dst", "").Replace("defense", "").Trim();
        if (t.Length == 0) return null;
        foreach (var (code, city, nick) in TeamNames)
        {
            if (exact)
            {
                if (t == nick || t == city || t == $"{city} {nick}" || t == $"{city.Split(' ')[0]} {nick}")
                    return code;
            }
            else if (t.Contains(nick) || t.Split(' ').Any(w => w.Equals(code, StringComparison.OrdinalIgnoreCase) || TeamCodes.Contains(w) && NflTeams.Code(w) == code))
                return code;
        }
        if (!exact)
            foreach (var (code, city, _) in TeamNames)
                if (t.StartsWith(city)) return code;
        return null;
    }

    // ---- matching ---------------------------------------------------------------------------

    public RosterMatch Match(string rawName, string? pos = null, string? team = null)
    {
        var teamCode = team is null ? null : NflTeams.Code(team);
        if (IsDefense(pos) || rawName.EndsWith("D/ST", StringComparison.OrdinalIgnoreCase))
        {
            var code = teamCode ?? DefenseCode(rawName);
            if (code is not null && _defByTeam.TryGetValue(code, out var def))
                return new RosterMatch(rawName, "DEF", code, def, MatchQuality.Exact);
            return new RosterMatch(rawName, "DEF", code, null, MatchQuality.Missing) { Suggestions = Suggest(rawName, "DEF", code, 5) };
        }

        var key = Key(rawName);
        var hintPos = NormalizePos(pos);

        if (_byKey.TryGetValue(key, out var exact))
        {
            var pick = Narrow(exact, hintPos, teamCode);
            if (pick is not null) return new RosterMatch(rawName, hintPos, teamCode, pick, MatchQuality.Exact);
        }

        var parts = key.Split(' ');
        if (parts.Length >= 2 && _byLast.TryGetValue(parts[^1], out var sameLast))
        {
            // Same last name and first initial ("D. Adams", "Gabe Davis" vs "Gabriel Davis").
            var initial = sameLast.Where(e => KeyOf(e).StartsWith(parts[0][0])).ToList();
            var pick = Narrow(initial, hintPos, teamCode);
            if (pick is not null) return new RosterMatch(rawName, hintPos, teamCode, pick, MatchQuality.Loose);
        }

        var suggestions = Suggest(rawName, hintPos, teamCode, 5);
        if (suggestions.Count > 0)
        {
            var best = Similarity(key, KeyOf(suggestions[0]));
            var second = suggestions.Count > 1 ? Similarity(key, KeyOf(suggestions[1])) : 0;
            if (best >= 0.86 && best - second >= 0.05)
                return new RosterMatch(rawName, hintPos, teamCode, suggestions[0], MatchQuality.Loose) { Suggestions = suggestions };
        }
        return new RosterMatch(rawName, hintPos, teamCode, null, MatchQuality.Missing) { Suggestions = suggestions };
    }

    // Best guesses for a free-typed search, used by the "fix a miss" box.
    public IReadOnlyList<DirectoryEntry> Suggest(string text, string? pos = null, string? team = null, int take = 6)
    {
        var key = Key(text);
        if (key.Length == 0) return [];
        var defCode = DefenseCode(text, exact: true);
        var scored = _all.Select(e =>
        {
            double score;
            if (IsDefense(e.Position))
                score = defCode is not null && NflTeams.Code(e.NflTeam) == defCode ? 1.0 : 0;
            else
            {
                var ek = KeyOf(e);
                if (ek.Length == 0) return (e, score: 0.0);
                score = Similarity(key, ek);
                if (ek.Contains(key)) score = Math.Max(score, 0.75 + 0.2 * key.Length / ek.Length);
                if (ek.Split(' ').Last() == key.Split(' ').Last()) score += 0.08;
            }
            if (pos is not null && NormalizePos(e.Position) == pos) score += 0.04;
            if (team is not null && NflTeams.Code(e.NflTeam) == team) score += 0.04;
            return (e, score);
        });
        return scored.Where(x => x.score > 0.55).OrderByDescending(x => x.score).ThenByDescending(x => x.e.TradeValue)
            .Take(take).Select(x => x.e).ToList();
    }

    string KeyOf(DirectoryEntry e) => _keyOf.TryGetValue(e, out var k) ? k : "";

    static DirectoryEntry? Narrow(List<DirectoryEntry> list, string? pos, string? team)
    {
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];
        IEnumerable<DirectoryEntry> f = list;
        if (pos is not null && f.Any(e => NormalizePos(e.Position) == pos)) f = f.Where(e => NormalizePos(e.Position) == pos);
        if (team is not null && f.Any(e => NflTeams.Code(e.NflTeam) == team)) f = f.Where(e => NflTeams.Code(e.NflTeam) == team);
        // Still ambiguous: the more valuable player is almost always the one on a fantasy roster.
        return f.OrderByDescending(e => e.TradeValue).First();
    }

    static string? NormalizePos(string? pos) => pos?.ToUpperInvariant() switch
    {
        null or "" or "FLEX" or "BN" or "BE" or "IR" or "OP" or "SF" => null,
        "PK" => "K",
        "DST" or "D/ST" => "DEF",
        var p => p,
    };

    // "Patrick Mahomes II" -> "patrick mahomes"; "Gabe Davis" -> "gabriel davis"; "A.J. Brown" -> "aj brown".
    public static string Key(string name)
    {
        var s = Fold(name).ToLowerInvariant();
        s = Regex.Replace(s, @"[.'’`]", "");
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Suffixes.Contains(t)).ToList();
        if (tokens.Count >= 2 && Nicknames.TryGetValue(tokens[0], out var full)) tokens[0] = full;
        return string.Join(' ', tokens);
    }

    static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    static double Similarity(string a, string b)
    {
        if (a == b) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    static void Add(Dictionary<string, List<DirectoryEntry>> map, string key, DirectoryEntry e)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(e);
    }

    [GeneratedRegex(@"([a-z])([A-Z][a-z]{1,2}|[A-Z]{2,3})\b")]
    private static partial Regex GluedTeam();
}
