// The data contract between CycloneHQ.Sync (writes these files) and CycloneHQ.App (reads them).
// Every file lives under the app's wwwroot/data/ and is serialized with JsonDefaults.Options.
// Lane B owns this file: extend it freely, but add fields rather than rename or remove them,
// and note any change in docs/PLAN.md so the app lane sees it.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CycloneHQ.Core.Models;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class DataPaths
{
    public const string Index = "data/leagues.json";
    public const string Directory = "data/directory.json";
    public static string League(string key) => $"data/{key}/league.json";
    public static string Players(string key) => $"data/{key}/players.json";
    public static string Teams(string key) => $"data/{key}/teams.json";
    public static string Live(string key) => $"data/{key}/live.json";
    public static string Analysis(string key) => $"data/{key}/analysis.json";
    public static string Transactions(string key) => $"data/{key}/transactions.json";
}

public static class Positions
{
    public static readonly string[] All = ["QB", "RB", "WR", "TE", "K", "DEF"];
}

public static class PlayerFlags
{
    public const string Underperforming = "underperforming"; // scoring well below what his slot needs
    public const string Underutilized = "underutilized";     // low snaps/targets/carries for his talent or value
    public const string BuyLow = "buy-low";                   // usage up, points lagging: likely to improve
    public const string SellHigh = "sell-high";               // points well above usage: likely to cool off
    public const string Breakout = "breakout";                // usage and points trending up sharply
    public const string Injured = "injured";
    public const string OnBye = "on-bye";
}

// data/leagues.json: every league synced automatically. Leagues George pastes in live only in his browser.
public sealed record LeagueIndex(DateTimeOffset UpdatedUtc, IReadOnlyList<LeagueRef> Leagues);

public sealed record LeagueRef(
    string Key,            // e.g. "mfl-24623"; also the folder name under data/
    string Platform,       // "mfl"
    string LeagueId,
    int Season,
    string Name,
    string MyFranchiseId,
    string MyTeamName);

// data/directory.json: compact list of every fantasy-relevant NFL player, used to match pasted rosters.
public sealed record PlayerDirectory(DateTimeOffset UpdatedUtc, IReadOnlyList<DirectoryEntry> Players);

public sealed record DirectoryEntry(
    string Id, string Name, string Position, string NflTeam,
    double PointsPerGame, double TradeValue, int? ConsensusPositionRank);

// data/{key}/league.json
public sealed record LeagueInfo(
    string Key,
    string Name,
    int Season,
    int CurrentWeek,
    IReadOnlyList<LineupSlot> Lineup,
    ScoringSummary Scoring,
    IReadOnlyList<Franchise> Franchises,
    DateTimeOffset UpdatedUtc);

public sealed record LineupSlot(string Position, int Min, int Max);

public sealed record ScoringSummary(double PointsPerReception, double PassTdPoints, string Description);

public sealed record Franchise(
    string Id,
    string Name,
    string? OwnerName,
    string? LogoUrl,
    int Wins, int Losses, int Ties,
    double PointsFor, double PointsAgainst,
    int StandingRank,
    IReadOnlyList<string> RosterPlayerIds);

// data/{key}/players.json: every rostered player in the league plus the best free agents.
public sealed record PlayerPool(DateTimeOffset UpdatedUtc, IReadOnlyList<Player> Players);

public sealed record Player(
    string Id,                 // MyFantasyLeague player id
    string Name,               // "First Last"
    string Position,
    string NflTeam,
    int? Age,
    int? ByeWeek,
    string? FantasyOwnerId,    // franchise id, or null for a free agent
    string? HeadshotUrl,
    InjuryInfo? Injury,
    PlayerStats Stats,
    PlayerUsage? Usage,
    PlayerRank Rank,
    IReadOnlyList<string> Flags);

public sealed record InjuryInfo(string Status, string? Details);

public sealed record PlayerStats(
    int GamesPlayed,
    double SeasonPoints,
    double PointsPerGame,
    double Last3PointsPerGame,
    double? ProjectedNextWeek,
    IReadOnlyList<WeeklyPoints> Weekly);

public sealed record WeeklyPoints(int Week, double Points, double? Expected);

public sealed record PlayerUsage(
    double? SnapPct,
    double? TargetShare,
    double? RushShare,
    int? RedZoneTouches,
    double? ExpectedPointsPerGame,
    double? PointsOverExpectedPerGame,
    string Source);

public sealed record PlayerRank(
    int? PositionRankLeague,      // rank at his position by points per game in this league's scoring
    int? ConsensusPositionRank,   // rest-of-season expert/consensus rank, where available
    double TradeValue,            // 0-100 rest-of-season value, comparable across positions
    string ValueSource);

// data/{key}/teams.json: how strong every team is, position by position.
public sealed record TeamProfiles(DateTimeOffset UpdatedUtc, IReadOnlyList<TeamProfile> Teams);

public sealed record TeamProfile(
    string FranchiseId,
    IReadOnlyList<string> OptimalStarterIds,
    double ProjectedStarterPoints,
    IReadOnlyList<PositionStrength> Positions,
    IReadOnlyList<string> Needs,     // positions where starters rank in the bottom third
    IReadOnlyList<string> Surplus);  // positions with tradeable depth beyond starting needs

public sealed record PositionStrength(
    string Position,
    double StarterScore,
    int LeagueRank,          // 1 = best in the league
    double VsLeagueMedian,   // + is better than the median team
    double DepthScore);

// data/{key}/live.json: this week's matchups.
public sealed record LiveWeek(int Week, DateTimeOffset UpdatedUtc, IReadOnlyList<LiveMatchup> Matchups);

public sealed record LiveMatchup(LiveSide Home, LiveSide Away);

public sealed record LiveSide(
    string FranchiseId,
    double Score,
    double? Projected,
    int PlayersYetToPlay,
    int GameSecondsRemaining,
    IReadOnlyList<LivePlayer> Players);

public sealed record LivePlayer(string PlayerId, bool IsStarter, double Score, int GameSecondsRemaining);

// data/{key}/analysis.json: the advice for George's own team.
public sealed record TeamAnalysis(
    string FranchiseId,
    DateTimeOffset UpdatedUtc,
    string Headline,
    IReadOnlyList<PositionNeed> Needs,
    IReadOnlyList<PlayerInsight> Insights);   // one per player on his roster

public sealed record PositionNeed(string Position, int LeagueRank, double GapVsMedian, string Note);

public sealed record PlayerInsight(
    string PlayerId,
    string Verdict,                       // "Start", "Bench", "Hold", "Buy low", "Sell", "Drop"
    string Summary,                       // one or two plain sentences
    IReadOnlyList<string> Reasons,
    IReadOnlyList<TradeIdea> TradeIdeas,  // best first
    IReadOnlyList<string> Replacements);  // free agent player ids worth a look

public sealed record TradeIdea(
    string PartnerFranchiseId,
    IReadOnlyList<string> Give,
    IReadOnlyList<string> Get,
    double ValueGive,
    double ValueGet,
    double MyLineupGainPpg,       // change in George's best-lineup points per game
    double PartnerLineupGainPpg,  // change for the other team: why they'd say yes
    double FitScore,              // 0-100, overall ranking score
    string Rationale);

// data/{key}/transactions.json
public sealed record TransactionLog(DateTimeOffset UpdatedUtc, IReadOnlyList<Transaction> Items);

public sealed record Transaction(
    DateTimeOffset When,
    string Type,
    string FranchiseId,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Dropped,
    string? Description);
