using System.Text.Json;
using Microsoft.JSInterop;

namespace CycloneHQ.App.Services.Leagues;

// A league George pasted or uploaded. It lives only in this browser's localStorage.
public sealed record ManualLeague(
    string Key,              // "manual-<slug>", the value passed to AppState.SelectLeague
    string Name,
    string Platform,         // "ESPN", "Yahoo", "Sleeper", "Other"
    int Teams,
    DateTimeOffset AddedUtc,
    IReadOnlyList<ManualRosterSpot> Roster);

public sealed record ManualRosterSpot(string RawName, string? PlayerId);

// localStorage can throw (private mode, blocked storage), so every access is wrapped and falls back to memory.
public sealed class ManualLeagues(IJSRuntime js)
{
    const string StorageKey = "cyclonehq.manualLeagues.v1";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    List<ManualLeague>? _leagues;

    public event Action? Changed;

    public async Task<IReadOnlyList<ManualLeague>> AllAsync()
    {
        if (_leagues is not null) return _leagues;
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            _leagues = string.IsNullOrWhiteSpace(raw) ? [] : JsonSerializer.Deserialize<List<ManualLeague>>(raw, Json) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ManualLeagues: storage unavailable ({ex.Message})");
            _leagues = [];
        }
        return _leagues;
    }

    public async Task<ManualLeague?> GetAsync(string key) => (await AllAsync()).FirstOrDefault(l => l.Key == key);

    public async Task<ManualLeague> SaveAsync(ManualLeague league)
    {
        var all = (await AllAsync()).ToList();
        all.RemoveAll(l => l.Key == league.Key);
        all.Add(league);
        await PersistAsync(all);
        return league;
    }

    public async Task DeleteAsync(string key)
    {
        var all = (await AllAsync()).ToList();
        all.RemoveAll(l => l.Key == key);
        await PersistAsync(all);
    }

    public async Task<string> UniqueKeyAsync(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        if (slug.Length == 0) slug = "league";
        var all = await AllAsync();
        var key = $"manual-{slug}";
        for (var i = 2; all.Any(l => l.Key == key); i++) key = $"manual-{slug}-{i}";
        return key;
    }

    async Task PersistAsync(List<ManualLeague> all)
    {
        _leagues = all;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(all, Json));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ManualLeagues: couldn't save ({ex.Message}); kept for this visit only");
        }
        Changed?.Invoke();
    }
}
