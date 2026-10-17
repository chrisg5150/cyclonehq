using CycloneHQ.Core.Models;
using CycloneHQ.Sync.Mfl;

namespace CycloneHQ.Sync.Enrichment;

// Expected stat lines for one player-week, keyed by MFL scoring event code (#P, PY, CC, ...),
// so each league can score them with its own rules.
sealed record ExpectedLine(int Week, Dictionary<string, double> Stats);

sealed record SeasonEnrichment(
    Dictionary<string, PlayerExtras> Extras,
    Dictionary<string, List<ExpectedLine>> Expected);

// Free, keyless sources layered over MFL's ranks:
//   nflverse      stats_player_week (target share), snap_counts (snap %)
//   ffopportunity ep_weekly (expected stat lines from play-by-play)
//   DynastyProcess values-players (trade values) and db_playerids (MFL id <-> gsis/pfr/FantasyPros ids)
static class PlayerEnrichment
{
    const string Nflverse = "https://github.com/nflverse/nflverse-data/releases/download";
    const string FfOpportunity = "https://github.com/ffverse/ffopportunity/releases/download/latest-data";
    const string DynastyProcess = "https://raw.githubusercontent.com/dynastyprocess/data/master/files";

    // ffopportunity column -> MFL scoring event it feeds.
    static readonly (string Column, string Event)[] ExpectedColumns =
    [
        ("pass_touchdown_exp", "#P"), ("pass_yards_gained_exp", "PY"), ("pass_two_point_conv_exp", "P2"),
        ("pass_interception_exp", "IN"),
        ("rush_touchdown_exp", "#R"), ("rush_yards_gained_exp", "RY"), ("rush_two_point_conv_exp", "R2"),
        ("rec_touchdown_exp", "#C"), ("rec_yards_gained_exp", "CY"), ("receptions_exp", "CC"),
        ("rec_two_point_conv_exp", "C2"),
    ];

    public static async Task<SeasonEnrichment> LoadAsync(HttpClient http, MflSeason season)
    {
        Log.Step("Usage and value sources");
        var extras = MflRankValues.From(season);
        var year = season.Season;

        var ids = await CsvTable.LoadAsync(http, $"{DynastyProcess}/db_playerids.csv", "DynastyProcess player ids");
        if (ids is null) return new(extras, []);
        Dictionary<string, string> MapToMfl(string column) => ids.Rows
            .Where(r => ids.Get(r, "mfl_id") is { Length: > 0 } m && season.Players.ContainsKey(m) && ids.Get(r, column).Length > 0)
            .DistinctBy(r => ids.Get(r, column))
            .ToDictionary(r => ids.Get(r, column), r => ids.Get(r, "mfl_id"));
        var fromGsis = MapToMfl("gsis_id");
        var fromPfr = MapToMfl("pfr_id");
        var fromFantasyPros = MapToMfl("fantasypros_id");

        var stats = await CsvTable.LoadAsync(http, $"{Nflverse}/stats_player/stats_player_week_{year}.csv", "nflverse player stats");
        var snaps = await CsvTable.LoadAsync(http, $"{Nflverse}/snap_counts/snap_counts_{year}.csv", "nflverse snap counts");
        var ep = await CsvTable.LoadAsync(http, $"{FfOpportunity}/ep_weekly_{year}.csv", "ffopportunity expected points");
        var values = await CsvTable.LoadAsync(http, $"{DynastyProcess}/values-players.csv", "DynastyProcess values");

        // Average per game across the regular season, keyed by MFL id.
        var snapPct = snaps is null ? [] : Average(snaps, fromPfr, "pfr_player_id", "offense_pct",
            r => snaps.Get(r, "game_type") == "REG" && snaps.Get(r, "position") is "QB" or "RB" or "FB" or "WR" or "TE");
        var targetShare = stats is null ? [] : Average(stats, fromGsis, "player_id", "target_share",
            r => stats.Get(r, "season_type") == "REG" && stats.Get(r, "position") is "RB" or "WR" or "TE");
        var rushShare = ep is null ? [] : ep.Rows
            .Where(r => fromGsis.ContainsKey(ep.Get(r, "player_id")))
            .GroupBy(r => fromGsis[ep.Get(r, "player_id")])
            .Where(g => g.Sum(r => ep.Num(r, "rush_attempt_team") ?? 0) > 0)
            .ToDictionary(g => g.Key, g => g.Sum(r => ep.Num(r, "rush_attempt") ?? 0) / g.Sum(r => ep.Num(r, "rush_attempt_team") ?? 0));
        var expected = ep is null ? [] : ep.Rows
            .Where(r => fromGsis.ContainsKey(ep.Get(r, "player_id")))
            .GroupBy(r => fromGsis[ep.Get(r, "player_id")])
            .ToDictionary(g => g.Key, g => g
                .Select(r => new ExpectedLine(
                    (int)(ep.Num(r, "week") ?? 0),
                    ExpectedColumns.ToDictionary(c => c.Event, c => ep.Num(r, c.Column) ?? 0)))
                .OrderBy(l => l.Week)
                .ToList());

        var usageSources = new[] { (snaps, "nflverse snaps"), (stats, "nflverse stats"), (ep, "ffopportunity") }
            .Where(s => s.Item1 is not null).Select(s => s.Item2);
        var usageSource = string.Join(" + ", usageSources);

        foreach (var id in snapPct.Keys.Union(targetShare.Keys).Union(rushShare.Keys))
        {
            var usage = new PlayerUsage(
                Round(snapPct.GetValueOrDefault(id, double.NaN)),
                Round(targetShare.GetValueOrDefault(id, double.NaN)),
                Round(rushShare.GetValueOrDefault(id, double.NaN)),
                RedZoneTouches: null,
                ExpectedPointsPerGame: null,       // filled per league, in that league's scoring
                PointsOverExpectedPerGame: null,
                usageSource);
            extras[id] = (extras.GetValueOrDefault(id) ?? new PlayerExtras(null, null, null, null)) with { Usage = usage };
        }

        if (values is not null) BlendTradeValues(values, fromFantasyPros, extras);
        Log.Info($"usage: {snapPct.Count} snap %, {targetShare.Count} target share, {rushShare.Count} rush share, {expected.Count} players with expected lines");
        return new(extras, expected);
    }

    static Dictionary<string, double> Average(CsvTable table, Dictionary<string, string> toMfl, string idColumn, string column, Func<string[], bool> keep) =>
        table.Rows
            .Where(r => keep(r) && toMfl.ContainsKey(table.Get(r, idColumn)) && table.Num(r, column) is not null)
            .GroupBy(r => toMfl[table.Get(r, idColumn)])
            .ToDictionary(g => g.Key, g => g.Average(r => table.Num(r, column)!.Value));

    // DynastyProcess values (FantasyPros ECR-based, 1QB) scaled to 0-100, averaged with MFL's
    // current-season rank value so long-term value doesn't swamp this year's production.
    static void BlendTradeValues(CsvTable values, Dictionary<string, string> fromFantasyPros, Dictionary<string, PlayerExtras> extras)
    {
        var rows = values.Rows
            .Where(r => fromFantasyPros.ContainsKey(values.Get(r, "fp_id")) && values.Num(r, "value_1qb") > 0)
            .Select(r => (Id: fromFantasyPros[values.Get(r, "fp_id")], Value: values.Num(r, "value_1qb")!.Value))
            .DistinctBy(x => x.Id)
            .ToList();
        if (rows.Count == 0) return;
        var max = rows.Max(r => r.Value);

        foreach (var (id, value) in rows)
        {
            var dp = 100 * value / max;
            var current = extras.GetValueOrDefault(id) ?? new PlayerExtras(null, null, null, null);
            extras[id] = current.TradeValue is double mflValue
                ? current with { TradeValue = Math.Round((dp + mflValue) / 2, 1), ValueSource = "DynastyProcess + MFL player ranks" }
                : current with { TradeValue = Math.Round(dp, 1), ValueSource = "DynastyProcess" };
        }
        Log.Info($"DynastyProcess trade values matched for {rows.Count} players");
    }

    static double? Round(double v) => double.IsNaN(v) ? null : Math.Round(v, 3);
}
