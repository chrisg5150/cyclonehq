using System.Text.Json;
using CycloneHQ.Core.Models;

namespace CycloneHQ.Sync;

// leagues.config.json: the MFL leagues to sync. Add an entry to sync another league.
sealed record SyncConfig(IReadOnlyList<LeagueConfig> Leagues)
{
    static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SyncConfig Load(string path) =>
        JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(path), ReadOptions)
        ?? throw new InvalidOperationException($"Could not read {path}");
}

sealed record LeagueConfig(string Platform, string LeagueId, int Season, string Host, string MyFranchiseId)
{
    public string Key => $"{Platform}-{LeagueId}";
}

// Writes contract files under the output folder. DataPaths are "data/..." relative to wwwroot,
// and --out points at wwwroot/data, so the leading "data/" is dropped.
sealed class DataWriter(string outDir)
{
    public void Write<T>(string dataPath, T value)
    {
        var path = Path.Combine(outDir, dataPath["data/".Length..]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonDefaults.Options));
        Log.Info($"wrote {dataPath} ({new FileInfo(path).Length / 1024.0:0.#} KB)");
    }
}

static class Log
{
    public static int Warnings { get; private set; }
    public static void Info(string message) => Console.WriteLine($"  {message}");
    public static void Step(string message) => Console.WriteLine($"> {message}");
    public static void Warn(string message)
    {
        Warnings++;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ! {message}");
        Console.ResetColor();
    }
}
