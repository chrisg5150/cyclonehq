using CycloneHQ.Core.Models;

namespace CycloneHQ.Sync.Mfl;

sealed record MflPlayer(string Id, string Name, string Position, string NflTeam, int? Age, string? EspnId)
{
    public bool HasNflTeam => !NflTeam.StartsWith("FA", StringComparison.Ordinal);
    public string? HeadshotUrl => EspnId is { Length: > 0 }
        ? $"https://a.espncdn.com/i/headshots/nfl/players/full/{EspnId}.png" : null;
}

// Everything MFL knows about the season that doesn't depend on a league:
// the player database, bye weeks, injuries and MFL's own weekly player ranks.
sealed class MflSeason
{
    public required int Season { get; init; }
    public required Dictionary<string, MflPlayer> Players { get; init; }
    public required Dictionary<string, int> ByeWeeks { get; init; }
    public required Dictionary<string, InjuryInfo> Injuries { get; init; }
    public required Dictionary<string, int> OverallRanks { get; init; }   // MFL playerRanks, 1 = best
    public required Dictionary<string, double> TopAddPercent { get; init; }

    public static async Task<MflSeason> LoadAsync(MflClient mfl, int season)
    {
        Log.Step($"MFL {season} season data");
        var api = MflClient.ApiHost;

        var players = (await mfl.ExportAsync(api, season, "players", "&DETAILS=1")).List("players", "player")
            .Select(p => new MflPlayer(
                p.Str("id"), DisplayName(p.Str("name")), Position(p.Str("position")), Team(p.Str("team")),
                Age(p.Str("birthdate")), NullIfEmpty(p.Str("espn_id"))))
            .Where(p => Positions.All.Contains(p.Position))
            .ToDictionary(p => p.Id);

        var byes = (await mfl.ExportAsync(api, season, "nflByeWeeks")).List("nflByeWeeks", "team")
            .ToDictionary(t => Team(t.Str("id")), t => t.Int("bye_week"));

        var injuries = (await mfl.ExportAsync(api, season, "injuries")).List("injuries", "injury")
            .Where(i => i.Str("status") != "RETIRED")
            .DistinctBy(i => i.Str("id"))
            .ToDictionary(i => i.Str("id"), i => new InjuryInfo(i.Str("status"), NullIfEmpty(i.Str("details"))));

        var ranks = (await mfl.ExportAsync(api, season, "playerRanks")).List("player_ranks", "player")
            .DistinctBy(r => r.Str("id"))
            .ToDictionary(r => r.Str("id"), r => r.Int("rank"));

        var adds = (await mfl.ExportAsync(api, season, "topAdds")).List("topAdds", "player")
            .DistinctBy(a => a.Str("id"))
            .ToDictionary(a => a.Str("id"), a => a.Num("percent"));

        Log.Info($"{players.Count} players, {injuries.Count} injuries, {ranks.Count} ranks, {byes.Count} bye weeks");
        return new() { Season = season, Players = players, ByeWeeks = byes, Injuries = injuries, OverallRanks = ranks, TopAddPercent = adds };
    }

    // MFL's team codes differ from ESPN's (NEP vs NE); store ESPN's so the app can join live NFL scores.
    public static string Team(string mfl) => mfl switch
    {
        "GBP" => "GB", "JAC" => "JAX", "KCC" => "KC", "LVR" => "LV", "NEP" => "NE",
        "NOS" => "NO", "SFO" => "SF", "TBB" => "TB", "WAS" => "WSH", _ => mfl,
    };

    // MFL uses "PK" for kickers and "Def" for team defenses.
    public static string Position(string mfl) => mfl switch { "PK" => "K", "Def" => "DEF", _ => mfl };

    // "Mahomes, Patrick" -> "Patrick Mahomes"; "Chiefs, Kansas City" -> "Kansas City Chiefs"
    static string DisplayName(string mfl) =>
        mfl.Split(", ", 2) is [var last, var first] ? $"{first} {last}" : mfl;

    static int? Age(string unixBirthdate)
    {
        if (!long.TryParse(unixBirthdate, out var seconds) || seconds == 0) return null;
        var born = DateTimeOffset.FromUnixTimeSeconds(seconds).Date;
        var today = DateTime.UtcNow.Date;
        var age = today.Year - born.Year;
        return born > today.AddYears(-age) ? age - 1 : age;
    }

    static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
