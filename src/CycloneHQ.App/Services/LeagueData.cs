using System.Net;
using System.Net.Http.Json;
using CycloneHQ.Core.Models;

namespace CycloneHQ.App.Services;

// Loads the synced JSON for the current league (wwwroot/data/{key}/...) and polls live.json every minute.
// Pages call EnsureLoadedAsync() and re-render on Changed. Lane E may use it too.
public sealed class LeagueData : IDisposable
{
    static readonly TimeSpan LivePoll = TimeSpan.FromSeconds(60);

    readonly HttpClient _http;
    readonly AppState _state;
    Task? _load;
    string? _loadedKey;
    PeriodicTimer? _timer;
    Dictionary<string, Player> _players = new();
    Dictionary<string, Franchise> _franchises = new();

    public LeagueData(HttpClient http, AppState state)
    {
        _http = http;
        _state = state;
        _state.Changed += OnLeagueChanged;
    }

    public event Action? Changed;

    public LeagueIndex? Index { get; private set; }
    public LeagueInfo? League { get; private set; }
    public PlayerPool? Pool { get; private set; }
    public TeamProfiles? Teams { get; private set; }
    public LiveWeek? Live { get; private set; }
    public TeamAnalysis? Analysis { get; private set; }
    public DateTimeOffset? LiveCheckedAt { get; private set; }

    public bool IsLoading => _load is { IsCompleted: false };
    public string? Error { get; private set; }
    public string Key => _state.CurrentLeagueKey;
    public bool IsManualLeague => Key.StartsWith("manual-", StringComparison.Ordinal);

    public LeagueRef? CurrentRef => Index?.Leagues.FirstOrDefault(l => l.Key == Key);
    public string LeagueName => League?.Name ?? CurrentRef?.Name ?? "My league";

    // "Universal League of American Fantasy Football" -> "ULAFF" for the header pill.
    public string LeagueShortName => LeagueName.Length <= 22 ? LeagueName
        : string.Concat(LeagueName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsUpper(w[0])).Select(w => w[0]));
    public string? MyFranchiseId => Analysis?.FranchiseId ?? CurrentRef?.MyFranchiseId;
    public Franchise? MyFranchise => MyFranchiseId is { } id ? Franchise(id) : null;
    public TeamProfile? MyProfile => Teams?.Teams.FirstOrDefault(t => t.FranchiseId == MyFranchiseId);

    public Player? Player(string? id) => id is not null && _players.TryGetValue(id, out var p) ? p : null;
    public Franchise? Franchise(string? id) => id is not null && _franchises.TryGetValue(id, out var f) ? f : null;
    public PlayerInsight? Insight(string id) => Analysis?.Insights.FirstOrDefault(i => i.PlayerId == id);

    public IEnumerable<Player> MyRoster =>
        MyFranchise?.RosterPlayerIds.Select(Player).OfType<Player>() ?? [];

    public LiveMatchup? MyMatchup => Live?.Matchups.FirstOrDefault(m =>
        m.Home.FranchiseId == MyFranchiseId || m.Away.FranchiseId == MyFranchiseId);

    public Task EnsureLoadedAsync()
    {
        if (_load is not null && _loadedKey == Key && Error is null) return _load;
        _loadedKey = Key;
        _load = LoadAsync(Key);
        _timer ??= StartPolling();
        return _load;
    }

    public Task RetryAsync()
    {
        Error = null;
        _load = null;
        return EnsureLoadedAsync();
    }

    async Task LoadAsync(string key)
    {
        Error = null;
        Changed?.Invoke();
        try
        {
            Index ??= await Get<LeagueIndex>(DataPaths.Index);
            if (IsManualLeague) { ClearLeague(); return; }

            var league = Get<LeagueInfo>(DataPaths.League(key));
            var pool = Get<PlayerPool>(DataPaths.Players(key));
            var teams = Get<TeamProfiles>(DataPaths.Teams(key));
            var live = Get<LiveWeek>(DataPaths.Live(key), bust: true);
            var analysis = Get<TeamAnalysis>(DataPaths.Analysis(key));
            await Task.WhenAll(league, pool, teams, live, analysis);
            if (key != Key) return; // the user switched leagues while this was loading

            League = league.Result;
            Pool = pool.Result;
            Teams = teams.Result;
            Live = live.Result;
            LiveCheckedAt = DateTimeOffset.Now;
            Analysis = analysis.Result;
            _players = Pool?.Players.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First()) ?? new();
            _franchises = League?.Franchises.ToDictionary(f => f.Id) ?? new();
            if (League is null || Pool is null)
                Error = "This league's data hasn't been synced yet.";
        }
        catch (Exception ex)
        {
            Error = "Couldn't load league data. Check your connection and try again.";
            Console.Error.WriteLine($"LeagueData: {ex.Message}");
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    void ClearLeague()
    {
        League = null; Pool = null; Teams = null; Live = null; Analysis = null;
        _players = new(); _franchises = new();
    }

    PeriodicTimer StartPolling()
    {
        var timer = new PeriodicTimer(LivePoll);
        _ = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (IsManualLeague || League is null) continue;
                try
                {
                    var key = Key;
                    var live = await Get<LiveWeek>(DataPaths.Live(key), bust: true);
                    if (key != Key || live is null) continue;
                    Live = live;
                    LiveCheckedAt = DateTimeOffset.Now;
                    Changed?.Invoke();
                }
                catch (Exception ex) { Console.Error.WriteLine($"Live poll: {ex.Message}"); }
            }
        });
        return timer;
    }

    // Relative paths only: the app is served from a GitHub Pages subpath. A missing file returns null.
    async Task<T?> Get<T>(string path, bool bust = false) where T : class
    {
        var url = bust ? $"{path}?v={DateTime.UtcNow.Ticks}" : path;
        using var res = await _http.GetAsync(url);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<T>(JsonDefaults.Options);
    }

    void OnLeagueChanged()
    {
        _load = null;
        ClearLeague();
        _ = EnsureLoadedAsync();
    }

    public void Dispose()
    {
        _state.Changed -= OnLeagueChanged;
        _timer?.Dispose();
    }
}
