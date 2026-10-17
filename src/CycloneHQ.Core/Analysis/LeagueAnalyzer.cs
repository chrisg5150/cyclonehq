using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// The single entry point Sync calls after it has built league.json and players.json.
// Lane D owns everything in this folder; lane B only calls Run(). Pure: no I/O.
public static class LeagueAnalyzer
{
    public static AnalysisResult Run(LeagueInfo league, PlayerPool pool, string myFranchiseId)
    {
        var now = DateTimeOffset.UtcNow;
        var ctx = LeagueContext.Build(league, pool);

        var flags = ctx.Players.Values.ToDictionary(r => r.Id, r => PlayerFlagger.Flags(ctx, r));
        var flaggedPool = new PlayerPool(pool.UpdatedUtc, pool.Players
            .Select(p => flags.TryGetValue(p.Id, out var f)
                ? p with { Flags = (p.Flags ?? []).Concat(f).Distinct().ToList() }
                : p)
            .ToList());

        var profiles = new TeamProfiles(now, league.Franchises
            .Where(f => ctx.Profiles.ContainsKey(f.Id))
            .Select(f => ctx.Profiles[f.Id])
            .ToList());

        if (!ctx.Profiles.ContainsKey(myFranchiseId))
            return new AnalysisResult(flaggedPool, profiles,
                new TeamAnalysis(myFranchiseId, now, "Couldn't find your team in this league's data.", [], []));

        var ideas = TradeFinder.FindAll(ctx, myFranchiseId);
        var assigned = TradeFinder.Assign(ideas, ctx.Rosters[myFranchiseId].Select(r => r.Id));
        var insights = ctx.Rosters[myFranchiseId]
            .OrderByDescending(r => ctx.Profiles[myFranchiseId].OptimalStarterIds.Contains(r.Id))
            .ThenByDescending(r => r.Score)
            .Select(r => InsightWriter.Write(ctx, myFranchiseId, r, flags[r.Id], assigned.GetValueOrDefault(r.Id) ?? []))
            .ToList();
        var (headline, needs) = InsightWriter.Headline(ctx, myFranchiseId, ideas);

        return new AnalysisResult(flaggedPool, profiles, new TeamAnalysis(myFranchiseId, now, headline, needs, insights));
    }
}

// Pool comes back with Flags filled in; Sync writes all three to disk.
public sealed record AnalysisResult(PlayerPool Pool, TeamProfiles Profiles, TeamAnalysis Analysis);
