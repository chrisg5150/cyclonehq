using System.Text;
using System.Text.Json;

namespace CycloneHQ.Sync.Mfl;

// Thin wrapper over MFL's export API. Requests go out one at a time with a short pause,
// and a failed export logs a warning and returns null so the rest of the sync carries on.
sealed class MflClient(HttpClient http)
{
    // League-independent exports (players, injuries, ranks, byes) must go to this host.
    public const string ApiHost = "https://api.myfantasyleague.com";

    static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(400);

    public async Task<JsonElement?> ExportAsync(string host, int season, string type, string query = "")
    {
        var url = $"{host.TrimEnd('/')}/{season}/export?TYPE={type}&JSON=1{query}";
        await Task.Delay(Pause);
        try
        {
            using var doc = JsonDocument.Parse(Decode(await http.GetByteArrayAsync(url)));
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.String ? error.GetString() : error.Str("$t");
                Log.Warn($"MFL {type}: {message}");
                return null;
            }
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.Warn($"MFL {type} failed: {ex.Message}");
            return null;
        }
    }

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // MFL says UTF-8, but older league text (team names typed in years ago) can be Latin-1.
    static string Decode(byte[] body)
    {
        try { return StrictUtf8.GetString(body); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(body); }
    }
}
