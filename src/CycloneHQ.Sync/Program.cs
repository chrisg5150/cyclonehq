using CycloneHQ.Core.Analysis;
using CycloneHQ.Core.Models;
using CycloneHQ.Sync;
using CycloneHQ.Sync.Enrichment;
using CycloneHQ.Sync.Mfl;

// Usage: dotnet run --project src/CycloneHQ.Sync -- --out src/CycloneHQ.App/wwwroot/data [--config path]
var outDir = Arg("--out") ?? "data";
var config = SyncConfig.Load(Arg("--config") ?? Path.Combine(AppContext.BaseDirectory, "leagues.config.json"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("CycloneHQ-Sync/1.0 (personal fantasy football dashboard)");

var mfl = new MflClient(http);
var writer = new DataWriter(outDir);
var synced = new List<LeagueRef>();
LeagueSyncResult? first = null;
MflSeason? firstSeason = null;
SeasonEnrichment? firstEnrichment = null;

foreach (var seasonLeagues in config.Leagues.Where(l => l.Platform == "mfl").GroupBy(l => l.Season))
{
    var season = await MflSeason.LoadAsync(mfl, seasonLeagues.Key);
    var enrichment = await PlayerEnrichment.LoadAsync(http, season);

    foreach (var cfg in seasonLeagues)
    {
        var result = await new MflLeagueSync(mfl, season, enrichment).RunAsync(cfg);
        if (result is null) continue;

        writer.Write(DataPaths.League(cfg.Key), result.League);
        writer.Write(DataPaths.Transactions(cfg.Key), result.Transactions);
        if (result.Live is not null) writer.Write(DataPaths.Live(cfg.Key), result.Live);

        var analyzed = Analyze(result, cfg.MyFranchiseId);
        writer.Write(DataPaths.Players(cfg.Key), analyzed.Pool);
        writer.Write(DataPaths.Teams(cfg.Key), analyzed.Profiles);
        writer.Write(DataPaths.Analysis(cfg.Key), analyzed.Analysis);

        synced.Add(result.Ref);
        if (first is null) (first, firstSeason, firstEnrichment) = (result, season, enrichment);
    }
}

writer.Write(DataPaths.Index, new LeagueIndex(DateTimeOffset.UtcNow, synced));
if (first is not null && firstSeason is not null && firstEnrichment is not null)
    writer.Write(DataPaths.Directory, PlayerDirectoryBuilder.Build(firstSeason, first, firstEnrichment.Extras));

Console.WriteLine($"Done: {synced.Count} league(s) synced, {Log.Warnings} warning(s).");
return synced.Count > 0 ? 0 : 1;

// The analysis engine is still evolving; if it throws, keep the raw data and write empty advice.
static AnalysisResult Analyze(LeagueSyncResult result, string myFranchiseId)
{
    try
    {
        return LeagueAnalyzer.Run(result.League, result.Pool, myFranchiseId);
    }
    catch (Exception ex)
    {
        Log.Warn($"LeagueAnalyzer failed, writing data without analysis: {ex.Message}");
        var now = DateTimeOffset.UtcNow;
        return new AnalysisResult(result.Pool, new TeamProfiles(now, []),
            new TeamAnalysis(myFranchiseId, now, "Analysis is unavailable right now.", [], []));
    }
}

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
