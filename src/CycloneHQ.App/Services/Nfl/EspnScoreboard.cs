using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace CycloneHQ.App.Services.Nfl;

// Live NFL scores straight from ESPN's public scoreboard (CORS allows browser requests).
public sealed class EspnScoreboard(HttpClient http)
{
    const string Url = "https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EspnBoard> LoadAsync(CancellationToken ct = default)
    {
        var board = await http.GetFromJsonAsync<EspnBoard>($"{Url}?_={DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond}", Json, ct);
        return board ?? new EspnBoard(null, []);
    }
}

// Just the slice of ESPN's scoreboard JSON we show.
public sealed record EspnBoard(EspnWeek? Week, List<EspnEvent> Events);

public sealed record EspnWeek(int Number, List<EspnTeam>? TeamsOnBye);

public sealed record EspnEvent(string Id, string Date, string? ShortName, List<EspnCompetition> Competitions)
{
    // ESPN sends "2026-10-18T17:00Z" (no seconds), which System.Text.Json won't parse as a date.
    public DateTimeOffset Kickoff => DateTimeOffset.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.MinValue;
    public EspnCompetition Game => Competitions[0];
}

public sealed record EspnCompetition(
    List<EspnCompetitor> Competitors,
    EspnStatus Status,
    List<EspnBroadcast>? Broadcasts,
    List<EspnOdds>? Odds,
    EspnSituation? Situation,
    EspnVenue? Venue)
{
    public EspnCompetitor Home => Competitors.First(c => c.HomeAway == "home");
    public EspnCompetitor Away => Competitors.First(c => c.HomeAway == "away");
    public string Tv => string.Join(", ", Broadcasts?.SelectMany(b => b.Names ?? []).Distinct() ?? []);
    public string Spread => Odds?.FirstOrDefault()?.Details ?? "";
    public double? OverUnder => Odds?.FirstOrDefault()?.OverUnder;
    public bool IsLive => Status.Type.State == "in";
    public bool IsFinal => Status.Type.State == "post";
    public bool IsPre => Status.Type.State == "pre";
}

public sealed record EspnCompetitor(string HomeAway, string? Score, EspnTeam Team, List<EspnRecord>? Records, bool? Winner)
{
    public string Record => Records?.FirstOrDefault()?.Summary ?? "";
    public int Points => int.TryParse(Score, out var s) ? s : 0;
}

public sealed record EspnTeam(string Id, string Abbreviation, string? DisplayName, string? ShortDisplayName, string? Logo, string? Color);

public sealed record EspnRecord(string? Summary);

public sealed record EspnStatus(string? DisplayClock, int Period, EspnStatusType Type);

public sealed record EspnStatusType(string? Name, string State, string? ShortDetail, string? Detail);

public sealed record EspnBroadcast(List<string>? Names);

public sealed record EspnOdds(string? Details, double? OverUnder);

public sealed record EspnSituation(string? Possession, string? DownDistanceText, string? ShortDownDistanceText, bool? IsRedZone);

public sealed record EspnVenue(string? FullName);
