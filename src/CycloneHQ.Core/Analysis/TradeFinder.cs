using CycloneHQ.Core.Models;

namespace CycloneHQ.Core.Analysis;

// Searches 1-for-1, 2-for-1 and 1-for-2 deals between my team and every other team, and scores them by
// how much they help my best lineup, whether the partner would plausibly say yes, value balance, and fit
// with each side's needs and surplus.
public static class TradeFinder
{
    public const double MinMyGain = 1.5;          // points per game; below this a deal isn't worth proposing...
    public const double MinNeedGain = 0.75;       // ...unless it shores up one of my weak lineup slots
    public const int MaxIdeasPerTarget = 2;       // across the whole analysis, so advice isn't all one player
    public const double MaxPartnerLoss = 1.0;     // partner can't lose more than this per game
    public const double MaxValueGap = 0.35;       // hard cap on value mismatch
    public const double FairValueGap = 0.15;      // no balance penalty within this
    const int ComboPool = 10;                     // players per side considered for 2-player packages

    public static List<TradeIdea> FindAll(LeagueContext ctx, string myId)
    {
        var ideas = new List<TradeIdea>();
        if (!ctx.Rosters.TryGetValue(myId, out var myRoster)) return ideas;
        var myBase = ctx.Lineups[myId].Points;
        var mine = Tradeable(myRoster);

        foreach (var (partnerId, theirRoster) in ctx.Rosters)
        {
            if (partnerId == myId) continue;
            var theirBase = ctx.Lineups[partnerId].Points;
            var theirs = Tradeable(theirRoster);

            var myCombos = Singles(mine).Concat(Pairs(mine)).ToList();
            var theirSingles = Singles(theirs).ToList();
            var theirPairs = Pairs(theirs).ToList();

            foreach (var give in myCombos)
            {
                // 1-for-1, 1-for-2, 2-for-1 and 2-for-2.
                var gets = theirSingles.Concat(theirPairs);
                foreach (var get in gets)
                {
                    var idea = Evaluate(ctx, myId, partnerId, myRoster, theirRoster, myBase, theirBase, give, get);
                    if (idea is not null) ideas.Add(idea);
                }
            }
        }
        return DropFiller(ideas.OrderByDescending(i => i.FitScore).ToList());
    }

    // A bigger version of a deal is only worth showing if the extra player actually changes something:
    // drop it when the same partner has a smaller deal (a subset of both sides) that scores about as well.
    static List<TradeIdea> DropFiller(List<TradeIdea> ranked)
    {
        static string Key(string partner, IEnumerable<string> give, IEnumerable<string> get) =>
            $"{partner}|{string.Join(",", give.Order())}|{string.Join(",", get.Order())}";
        static IEnumerable<string[]> Subsets(IReadOnlyList<string> xs) => xs.Count == 1
            ? [[xs[0]]]
            : [[xs[0]], [xs[1]], [xs[0], xs[1]]];

        var byKey = ranked.ToDictionary(i => Key(i.PartnerFranchiseId, i.Give, i.Get), i => i.FitScore);
        return ranked.Where(i =>
        {
            if (i.Give.Count + i.Get.Count <= 2) return true;
            foreach (var g in Subsets(i.Give))
                foreach (var x in Subsets(i.Get))
                {
                    if (g.Length == i.Give.Count && x.Length == i.Get.Count) continue;
                    if (byKey.TryGetValue(Key(i.PartnerFranchiseId, g, x), out var fit) && fit >= i.FitScore - 1) return false;
                }
            return true;
        }).ToList();
    }

    // Hands out ideas to my players round-robin so the advice stays varied: each target player appears in at
    // most two distinct ideas across the whole analysis, and a player's list spreads across partners
    // (one per partner for the first three picks, two at most).
    public static Dictionary<string, List<TradeIdea>> Assign(IReadOnlyList<TradeIdea> ranked, IEnumerable<string> myPlayerIds, int maxPerPlayer = 5)
    {
        var players = myPlayerIds
            .OrderByDescending(id => ranked.FirstOrDefault(i => i.Give.Contains(id))?.FitScore ?? -1)
            .ToList();
        var lists = players.ToDictionary(id => id, _ => new List<TradeIdea>());
        var chosen = new HashSet<TradeIdea>(ReferenceEqualityComparer.Instance);
        var targetUses = new Dictionary<string, int>();

        for (var round = 0; round < maxPerPlayer; round++)
        {
            var perPartnerCap = round < 3 ? 1 : 2;
            foreach (var id in players)
            {
                var list = lists[id];
                if (list.Count < round) continue;   // this player ran out of ideas earlier
                var pick = ranked.FirstOrDefault(i =>
                    i.Give.Contains(id) && !list.Contains(i) &&
                    list.Count(x => x.PartnerFranchiseId == i.PartnerFranchiseId) < perPartnerCap &&
                    (chosen.Contains(i) || i.Get.All(g => targetUses.GetValueOrDefault(g) < MaxIdeasPerTarget)));
                if (pick is null) continue;
                list.Add(pick);
                if (chosen.Add(pick))
                    foreach (var g in pick.Get) targetUses[g] = targetUses.GetValueOrDefault(g) + 1;
            }
        }
        return lists;
    }

    public static TradeIdea? Evaluate(
        LeagueContext ctx, string myId, string partnerId,
        List<RatedPlayer> myRoster, List<RatedPlayer> theirRoster, double myBase, double theirBase,
        IReadOnlyList<RatedPlayer> give, IReadOnlyList<RatedPlayer> get)
    {
        var valueGive = give.Sum(p => p.Value);
        var valueGet = get.Sum(p => p.Value);
        var maxV = Math.Max(valueGive, valueGet);
        if (maxV <= 0) return null;
        var gap = Math.Abs(valueGive - valueGet) / maxV;
        if (gap > MaxValueGap) return null;
        // Swapping one QB for another is a sideways move, not a trade worth proposing.
        if (give.All(p => p.Pos == "QB") && get.All(p => p.Pos == "QB")) return null;

        var giveIds = give.Select(p => p.Id).ToHashSet();
        var getIds = get.Select(p => p.Id).ToHashSet();
        var myAfter = myRoster.Where(p => !giveIds.Contains(p.Id)).Concat(get);
        var theirAfter = theirRoster.Where(p => !getIds.Contains(p.Id)).Concat(give);
        var myLineup = LineupOptimizer.Best(myAfter, ctx.Rules);
        var myGain = myLineup.Points - myBase;
        if (myGain < MinNeedGain) return null;
        // No padding: every incoming player has to start for me or be a real piece of the value.
        if (get.Count > 1 && get.Any(p => !myLineup.Contains(p.Id) && p.Value < 0.25 * valueGet)) return null;
        var myNeeds = ctx.NeedLevelsCached(myId);
        var fillsMyNeed = get.Where(p => myLineup.Contains(p.Id)).Select(p => myNeeds.GetValueOrDefault(p.Pos)).DefaultIfEmpty(0).Max();
        if (myGain < MinMyGain && !get.Any(p => myLineup.Contains(p.Id) && ctx.UrgentNeedPositions(myId).Contains(p.Pos))) return null;
        var theirLineup = LineupOptimizer.Best(theirAfter, ctx.Rules);
        var theirGain = theirLineup.Points - theirBase;
        if (theirGain < -MaxPartnerLoss) return null;

        var me = ctx.Profiles[myId];
        var them = ctx.Profiles[partnerId];
        var theirNeeds = ctx.NeedLevelsCached(partnerId);
        var fillsTheirNeed = give.Where(p => theirLineup.Contains(p.Id)).Select(p => theirNeeds.GetValueOrDefault(p.Pos)).DefaultIfEmpty(0).Max();
        var fit = FitScore(myGain, theirGain, gap, Fit(me, them, give, get, fillsMyNeed, fillsTheirNeed));
        var rationale = Rationale(ctx, me, them, give, get, myLineup, myGain, theirGain, fillsMyNeed, fillsTheirNeed);
        return new TradeIdea(
            partnerId,
            give.Select(p => p.Id).ToList(),
            get.Select(p => p.Id).ToList(),
            Math.Round(valueGive, 1), Math.Round(valueGet, 1),
            Math.Round(myGain, 2), Math.Round(theirGain, 2),
            Math.Round(fit, 1), rationale);
    }

    // 0-100: needs fit and my lineup gain carry the most weight, then value balance and whether they'd accept.
    public static double FitScore(double myGain, double theirGain, double valueGap, double needFit)
    {
        var mineScore = Math.Clamp(myGain / 3.0, 0, 1);
        var theirScore = Math.Clamp((theirGain + MaxPartnerLoss) / 2.5, 0, 1);
        var balance = valueGap <= FairValueGap ? 1 : Math.Clamp(1 - (valueGap - FairValueGap) / (MaxValueGap - FairValueGap), 0, 1);
        return 30 * mineScore + 15 * theirScore + 20 * balance + 35 * needFit;
    }

    // 0-1: does an incoming starter fill one of my weak lineup slots, paid for with my depth, and does what I
    // send fill one of theirs, out of their depth? Filling my weakest slot counts the most.
    static double Fit(TeamProfile me, TeamProfile them, IReadOnlyList<RatedPlayer> give, IReadOnlyList<RatedPlayer> get,
        double fillsMyNeed, double fillsTheirNeed)
    {
        double fromMyDepth = give.Average(p => me.Surplus.Contains(p.Pos) || !me.OptimalStarterIds.Contains(p.Id) ? 1.0 : 0.0);
        double fromTheirDepth = get.Average(p => them.Surplus.Contains(p.Pos) ? 1.0 : !them.OptimalStarterIds.Contains(p.Id) ? 0.7 : 0.0);
        return 0.5 * fillsMyNeed + 0.15 * fromMyDepth + 0.2 * fillsTheirNeed + 0.15 * fromTheirDepth;
    }

    static string Rationale(LeagueContext ctx, TeamProfile me, TeamProfile them, IReadOnlyList<RatedPlayer> give,
        IReadOnlyList<RatedPlayer> get, Lineup myLineup, double myGain, double theirGain, double fillsMyNeed, double fillsTheirNeed)
    {
        var partner = ctx.TeamName(them.FranchiseId);
        // Name only the incoming players who actually make my new best lineup, and the spots they fill.
        var starters = get.Where(p => myLineup.Contains(p.Id)).OrderByDescending(p => p.Value).ToList();
        if (starters.Count == 0) starters = [get.OrderByDescending(p => p.Value).First()];
        var who = string.Join(" and ", starters.Select(p => p.Name));
        var verb = starters.Count > 1 ? "upgrade" : "upgrades";
        var spots = starters.Select(p => p.Pos).Distinct().ToList();
        var weakest = ctx.WeakestSlot(me.FranchiseId);
        var mine = fillsMyNeed >= 1 && weakest is not null && starters.Count == 1
                   ? $"({string.Join("/", starters.Select(p => p.Pos).Distinct())}) {verb} your weakest spot, {(weakest.Slot.StartsWith("Flex") ? "the " : "")}{SlotName(weakest.Slot)},"
                 : spots.Count > 1 ? $"{verb} your {string.Join(" and ", spots)} spots"
                 : $"{verb} your {spots[0]} spot";
        var benchGiven = give.Count(p => !me.OptimalStarterIds.Contains(p.Id));
        var cost = benchGiven == give.Count ? " using bench depth"
                 : give.Count > get.Count && benchGiven > 0 ? " by consolidating depth"
                 : give.Count > get.Count ? " by consolidating two starters" : "";
        var theirNeeds = ctx.NeedLevelsCached(them.FranchiseId);
        var theirPos = give.Select(p => p.Pos).FirstOrDefault(p => theirNeeds.GetValueOrDefault(p) > 0)
                       ?? give.OrderByDescending(p => p.Score).First().Pos;
        var theirSide = theirGain >= 0.3
            ? (fillsTheirNeed > 0
                ? $"it fills a {theirPos} need for {partner} (+{theirGain:0.0} for them)"
                : $"it helps {partner} too (+{theirGain:0.0} for them)")
            : theirGain >= -0.3
                ? $"it's lineup-neutral for {partner} with fair value coming back"
                : $"it costs {partner} only {-theirGain:0.0} pts/week for {(give.Count > get.Count ? "extra depth" : "fair value")}";
        return $"{who} {mine} by {myGain:0.0} pts/week{cost}, and {theirSide}.";
    }

    // "Flex" -> "flex", "WR/TE #3" -> "WR/TE #3", "QB" -> "QB".
    public static string SlotName(string slot) => slot.StartsWith("Flex") ? slot.ToLowerInvariant() : slot;

    static List<RatedPlayer> Tradeable(IEnumerable<RatedPlayer> roster) => roster
        .Where(p => !PlayerValuation.NonTradePositions.Contains(p.Pos) && (p.Value >= 3 || p.Score >= 3))
        .OrderByDescending(p => p.Value)
        .ToList();

    static IEnumerable<IReadOnlyList<RatedPlayer>> Singles(List<RatedPlayer> xs) => xs.Select(x => (IReadOnlyList<RatedPlayer>)[x]);

    static IEnumerable<IReadOnlyList<RatedPlayer>> Pairs(List<RatedPlayer> xs)
    {
        var top = xs.Take(ComboPool).ToList();
        for (var i = 0; i < top.Count; i++)
            for (var j = i + 1; j < top.Count; j++)
                yield return [top[i], top[j]];
    }
}
