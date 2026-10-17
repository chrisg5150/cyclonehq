using System.Net;
using System.Net.Http.Json;
using CycloneHQ.Core.Models;

namespace CycloneHQ.App.Services.Leagues;

// The synced files lane A's LeagueData doesn't load: the player directory (for pasted rosters)
// and each league's transaction log. Cached per path; a missing file is null and is retried next time.
public sealed class LeagueExtras(HttpClient http)
{
    readonly Dictionary<string, Task<object?>> _cache = new();

    public Task<PlayerDirectory?> DirectoryAsync() => Get<PlayerDirectory>(DataPaths.Directory);
    public Task<LeagueIndex?> IndexAsync() => Get<LeagueIndex>(DataPaths.Index);
    public Task<TransactionLog?> TransactionsAsync(string key) => Get<TransactionLog>(DataPaths.Transactions(key));
    public Task<PlayerPool?> PlayersAsync(string key) => Get<PlayerPool>(DataPaths.Players(key));

    async Task<T?> Get<T>(string path) where T : class
    {
        if (!_cache.TryGetValue(path, out var task))
            _cache[path] = task = Load<T>(path);
        var result = (T?)await task;
        if (result is null) _cache.Remove(path);
        return result;
    }

    // Relative paths only: the app is served from a GitHub Pages subpath.
    async Task<object?> Load<T>(string path) where T : class
    {
        try
        {
            using var res = await http.GetAsync(path);
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<T>(JsonDefaults.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LeagueExtras {path}: {ex.Message}");
            return null;
        }
    }
}
