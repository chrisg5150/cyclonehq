namespace CycloneHQ.App.Services;

// Shared between lane A's and lane E's pages: which league is on screen.
// Keys match LeagueRef.Key for synced leagues ("mfl-24623") or "manual-<slug>" for pasted ones.
// Lane A owns this file; lane E may read it and call SelectLeague.
public sealed class AppState
{
    public string CurrentLeagueKey { get; private set; } = "mfl-24623";

    public event Action? Changed;

    public void SelectLeague(string key)
    {
        if (key == CurrentLeagueKey) return;
        CurrentLeagueKey = key;
        Changed?.Invoke();
    }
}
