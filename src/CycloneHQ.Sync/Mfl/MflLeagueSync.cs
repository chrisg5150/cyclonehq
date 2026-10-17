using System.Text.Json;
using CycloneHQ.Core.Models;
using CycloneHQ.Sync.Enrichment;

namespace CycloneHQ.Sync.Mfl;

sealed record LeagueSyncResult(
    LeagueRef Ref,
    LeagueInfo League,
    PlayerPool Pool,
    LiveWeek? Live,
    TransactionLog Transactions,
    IReadOnlyDictionary<string, PlayerStats> AllStats,      // every player who has scored, in this league's scoring
    IReadOnlyDictionary<string, int> PositionRanks);

// Pulls one MFL league and maps it onto the CycloneHQ data contract.
sealed class MflLeagueSync(MflClient mfl, MflSeason season, SeasonEnrichment enrichment)
{
    const int FreeAgentsInPool = 80;
    readonly Dictionary<string, MflScoringRules> _scoring = [];

    public async Task<LeagueSyncResult?> RunAsync(LeagueConfig cfg)
    {
        Log.Step($"MFL league {cfg.LeagueId} ({cfg.Season})");
        Task<JsonElement?> Export(string type, string query = "") =>
            mfl.ExportAsync(cfg.Host, cfg.Season, type, $"&L={cfg.LeagueId}{query}");

        var league = (await Export("league")).Get("league");
        if (league is null)
        {
            Log.Warn($"league {cfg.LeagueId}: no league export, skipping");
            return null;
        }
        var rosters = await Export("rosters");
        var standings = await Export("leagueStandings");
        var live = await Export("liveScoring");
        var rules = await Export("rules");
        var freeAgents = await Export("freeAgents");
        var transactions = await Export("transactions");

        var currentWeek = WeekOf(live.Get("liveScoring")) ?? WeekOf(rosters.List("rosters", "franchise").FirstOrDefault()) ?? 1;

        // Weekly fantasy points in this league's scoring: week -> player id -> points.
        var weekly = new SortedDictionary<int, Dictionary<string, double>>();
        for (var week = 1; week <= currentWeek; week++)
        {
            var scores = await Export("playerScores", $"&YEAR={cfg.Season}&W={week}");
            weekly[week] = scores.List("playerScores", "playerScore")
                .DistinctBy(s => s.Str("id"))
                .ToDictionary(s => s.Str("id"), s => s.Num("score"));
        }
        var projected = (await Export("projectedScores", $"&W={currentWeek}")).List("projectedScores", "playerScore")
            .DistinctBy(s => s.Str("id"))
            .ToDictionary(s => s.Str("id"), s => s.Num("score"));

        foreach (var position in Positions.All) _scoring[position] = MflScoringRules.For(rules, position);
        var allStats = weekly.Values.SelectMany(w => w.Keys).Distinct()
            .ToDictionary(id => id, id => StatsFor(id, weekly, projected));
        var positionRanks = RankByPointsPerGame(allStats, minGames: Math.Max(1, (currentWeek - 1) / 2));

        // Rosters and the pool: everyone rostered plus the best free agents.
        var rosterIds = rosters.List("rosters", "franchise").ToDictionary(
            f => f.Str("id"),
            f => f.List("player").Select(p => p.Str("id")).ToList());
        var ownerOf = rosterIds.SelectMany(r => r.Value.Select(id => (id, owner: r.Key)))
            .DistinctBy(x => x.id).ToDictionary(x => x.id, x => x.owner);
        var topFreeAgents = freeAgents.List("freeAgents", "leagueUnit", "player")
            .Select(p => p.Str("id"))
            .Where(season.Players.ContainsKey)
            .OrderByDescending(id => allStats.GetValueOrDefault(id)?.SeasonPoints ?? 0)
            .Take(FreeAgentsInPool);

        var now = DateTimeOffset.UtcNow;
        var players = ownerOf.Keys.Concat(topFreeAgents)
            .Select(id => BuildPlayer(id, ownerOf.GetValueOrDefault(id), allStats, positionRanks, projected, currentWeek))
            .ToList();

        var name = league.Value.Str("name");
        var franchises = BuildFranchises(league, standings, rosterIds);
        var info = new LeagueInfo(cfg.Key, name, cfg.Season, currentWeek, Lineup(league), Scoring(rules), franchises, now);
        var myTeam = franchises.FirstOrDefault(f => f.Id == cfg.MyFranchiseId)?.Name ?? cfg.MyFranchiseId;

        Log.Info($"{name}: week {currentWeek}, {franchises.Count} teams, {players.Count} players in pool");
        return new LeagueSyncResult(
            new LeagueRef(cfg.Key, cfg.Platform, cfg.LeagueId, cfg.Season, name, cfg.MyFranchiseId, myTeam),
            info,
            new PlayerPool(now, players),
            BuildLive(live, currentWeek, projected, now),
            new TransactionLog(now, BuildTransactions(transactions)),
            allStats,
            positionRanks);
    }

    static int? WeekOf(JsonElement? e) => e is { } x && x.Int("week") is > 0 and var w ? w : null;

    PlayerStats StatsFor(string id, SortedDictionary<int, Dictionary<string, double>> weekly, Dictionary<string, double> projected)
    {
        // Expected points: ffopportunity's expected stat line, scored with this league's rules.
        var expected = enrichment.Expected.GetValueOrDefault(id) is { } lines
            && season.Players.GetValueOrDefault(id)?.Position is { } pos && _scoring.GetValueOrDefault(pos) is { } rules
            ? lines.ToDictionary(l => l.Week, l => Math.Round(rules.Score(l.Stats), 1))
            : [];
        // MFL lists a player for a week only when he recorded stats, so an entry = a game played
        // (TD-heavy scoring means a played game can still be worth 0).
        var games = weekly
            .Where(w => w.Value.ContainsKey(id))
            .Select(w => new WeeklyPoints(w.Key, w.Value[id], expected.TryGetValue(w.Key, out var x) ? x : null))
            .ToList();
        var total = games.Sum(g => g.Points);
        var last3 = games.TakeLast(3).ToList();
        return new PlayerStats(
            GamesPlayed: games.Count,
            SeasonPoints: total,
            PointsPerGame: games.Count > 0 ? Math.Round(total / games.Count, 2) : 0,
            Last3PointsPerGame: last3.Count > 0 ? Math.Round(last3.Average(g => g.Points), 2) : 0,
            ProjectedNextWeek: projected.TryGetValue(id, out var p) ? p : null,
            Weekly: games);
    }

    Dictionary<string, int> RankByPointsPerGame(Dictionary<string, PlayerStats> stats, int minGames) =>
        stats.Where(s => s.Value.GamesPlayed >= minGames && season.Players.ContainsKey(s.Key))
            .GroupBy(s => season.Players[s.Key].Position)
            .SelectMany(g => g.OrderByDescending(s => s.Value.PointsPerGame).Select((s, i) => (s.Key, Rank: i + 1)))
            .ToDictionary(x => x.Key, x => x.Rank);

    Player BuildPlayer(string id, string? owner, Dictionary<string, PlayerStats> allStats,
        Dictionary<string, int> positionRanks, Dictionary<string, double> projected, int currentWeek)
    {
        var p = season.Players.GetValueOrDefault(id) ?? new MflPlayer(id, $"Player {id}", "?", "FA", null, null);
        var stats = allStats.GetValueOrDefault(id)
            ?? new PlayerStats(0, 0, 0, 0, projected.TryGetValue(id, out var proj) ? proj : null, []);
        var injury = season.Injuries.GetValueOrDefault(id);
        int? bye = season.ByeWeeks.TryGetValue(p.NflTeam, out var b) ? b : null;
        var extra = enrichment.Extras.GetValueOrDefault(id);

        var flags = new List<string>();
        if (injury is { Status: not ("Questionable" or "Probable") }) flags.Add(PlayerFlags.Injured);
        if (bye == currentWeek) flags.Add(PlayerFlags.OnBye);

        return new Player(
            id, p.Name, p.Position, p.NflTeam, p.Age, bye, owner, p.HeadshotUrl, injury, stats,
            WithExpected(extra?.Usage, stats),
            new PlayerRank(
                positionRanks.TryGetValue(id, out var r) ? r : null,
                extra?.ConsensusPositionRank,
                extra?.TradeValue ?? 0,
                extra?.ValueSource ?? "none"),
            flags);
    }

    // Adds expected points per game and points over expected (both in league scoring) to usage.
    static PlayerUsage? WithExpected(PlayerUsage? usage, PlayerStats stats)
    {
        var weeks = stats.Weekly.Where(w => w.Expected is not null).ToList();
        if (weeks.Count == 0) return usage;
        usage ??= new PlayerUsage(null, null, null, null, null, null, "ffopportunity");
        return usage with
        {
            ExpectedPointsPerGame = Math.Round(weeks.Average(w => w.Expected!.Value), 2),
            PointsOverExpectedPerGame = Math.Round(weeks.Average(w => w.Points - w.Expected!.Value), 2),
        };
    }

    static List<Franchise> BuildFranchises(JsonElement? league, JsonElement? standings, Dictionary<string, List<string>> rosterIds)
    {
        var rows = standings.List("leagueStandings", "franchise").ToDictionary(f => f.Str("id"));
        var franchises = league.List("franchises", "franchise").Select(f =>
        {
            var s = rows.GetValueOrDefault(f.Str("id"));
            var logo = f.Str("logo") is { Length: > 0 } l ? l : f.Str("icon");
            return new Franchise(
                f.Str("id"), f.Str("name"),
                f.Str("owner_name") is { Length: > 0 } o ? o : null,
                logo.Length > 0 ? logo : null,
                s.Int("h2hw"), s.Int("h2hl"), s.Int("h2ht"), s.Num("pf"), s.Num("pa"),
                StandingRank: 0,
                rosterIds.GetValueOrDefault(f.Str("id")) ?? []);
        }).ToList();

        // Rank by win percentage, then points for (the league's own sort is PCT, DIVPCT, H2H, PTS).
        return franchises
            .OrderByDescending(f => f.Wins + f.Losses + f.Ties == 0 ? 0 : (f.Wins + f.Ties / 2.0) / (f.Wins + f.Losses + f.Ties))
            .ThenByDescending(f => f.PointsFor)
            .Select((f, i) => f with { StandingRank = i + 1 })
            .ToList();
    }

    // starters: { position: [{ name: "QB", limit: "1" }, { name: "RB", limit: "1-2" }, ...] }
    static List<LineupSlot> Lineup(JsonElement? league) =>
        league.List("starters", "position").Select(p =>
        {
            var limits = p.Str("limit").Split('-');
            var min = int.TryParse(limits[0], out var lo) ? lo : 0;
            var max = limits.Length > 1 && int.TryParse(limits[1], out var hi) ? hi : min;
            return new LineupSlot(MflSeason.Position(p.Str("name")), min, max);
        }).ToList();

    // Reads the QB/RB/WR/TE scoring rules: "*6" means 6 points each; plain numbers are tiered bonuses.
    static ScoringSummary Scoring(JsonElement? rules)
    {
        var offense = rules.List("rules", "positionRules")
            .FirstOrDefault(r => r.Str("positions").Split('|').Contains("WR"));
        var rule = offense.ValueKind == JsonValueKind.Object ? offense.List("rule").ToList() : [];

        double PerEach(string evt) => rule.Where(r => r.Str("event") == evt && r.Str("points").StartsWith('*'))
            .Select(r => double.TryParse(r.Str("points")[1..], out var v) ? v : 0).FirstOrDefault();
        bool Tiered(string evt) => rule.Count(r => r.Str("event") == evt && !r.Str("points").StartsWith('*')) > 1;

        var ppr = PerEach("CC");
        var passTd = PerEach("#P");
        var parts = new List<string> { $"{passTd:0}-pt passing TDs", $"{PerEach("#R"):0}-pt rushing and receiving TDs" };
        parts.Add(ppr > 0 ? $"{ppr:0.##} per catch" : Tiered("CC") ? "no PPR (catch bonuses from 6+ catches)" : "no PPR");
        if (Tiered("PY") || Tiered("RY") || Tiered("CY")) parts.Add("yardage scored in bonus tiers");
        return new ScoringSummary(ppr, passTd, string.Join(", ", parts));
    }

    static LiveWeek? BuildLive(JsonElement? live, int week, Dictionary<string, double> projected, DateTimeOffset now)
    {
        var matchups = live.List("liveScoring", "matchup").Select(m =>
        {
            var sides = m.List("franchise").Select(f => (Home: f.Str("isHome") == "1", Side: Side(f))).ToList();
            if (sides.Count != 2) return null;
            var home = sides.FirstOrDefault(s => s.Home).Side ?? sides[0].Side;
            var away = sides.First(s => s.Side != home).Side;
            return new LiveMatchup(home, away);
        }).OfType<LiveMatchup>().ToList();
        return matchups.Count > 0 ? new LiveWeek(week, now, matchups) : null;

        LiveSide Side(JsonElement f)
        {
            var players = f.List("players", "player").Select(p => new LivePlayer(
                p.Str("id"), p.Str("status") == "starter", p.Num("score"), p.Int("gameSecondsRemaining"))).ToList();
            // Projection: points so far plus each starter's projection for the time he has left.
            var proj = players.Where(p => p.IsStarter)
                .Sum(p => p.Score + projected.GetValueOrDefault(p.PlayerId) * p.GameSecondsRemaining / 3600.0);
            return new LiveSide(f.Str("id"), f.Num("score"), Math.Round(proj, 1),
                f.Int("playersYetToPlay"), f.Int("gameSecondsRemaining"), players);
        }
    }

    static List<Transaction> BuildTransactions(JsonElement? transactions) =>
        transactions.List("transactions", "transaction")
            .Where(t => t.Str("franchise").Length > 0)
            .Select(t =>
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(t.Str("timestamp"), out var ts) ? ts : 0);
                var type = t.Str("type");
                var parts = t.Str("transaction").Split('|');
                string Part(int i) => i < parts.Length ? parts[i] : "";
                return type switch
                {
                    // "adds|drops"
                    "FREE_AGENT" => new Transaction(when, type, t.Str("franchise"), MflJson.Ids(Part(0)), MflJson.Ids(Part(1)), null),
                    // "adds|bid|drops"
                    "BBID_WAIVER" => new Transaction(when, type, t.Str("franchise"), MflJson.Ids(Part(0)), MflJson.Ids(Part(2)), $"Waiver bid ${Part(1)}"),
                    // "player|bid|"
                    "AUCTION_WON" => new Transaction(when, type, t.Str("franchise"), MflJson.Ids(Part(0)), [], $"Won at auction for ${Part(1)}"),
                    "IR" => new Transaction(when, type, t.Str("franchise"), MflJson.Ids(t.Str("activated")), MflJson.Ids(t.Str("deactivated")),
                        "Injured reserve move (added = activated, dropped = moved to IR)"),
                    // Seen from the first franchise's side.
                    "TRADE" => new Transaction(when, type, t.Str("franchise"), MflJson.Ids(t.Str("franchise2_gave_up")), MflJson.Ids(t.Str("franchise1_gave_up")),
                        $"Trade with {t.Str("franchise2")}"),
                    _ => new Transaction(when, type, t.Str("franchise"), [], [], null),
                };
            })
            .OrderByDescending(t => t.When)
            .ToList();
}
