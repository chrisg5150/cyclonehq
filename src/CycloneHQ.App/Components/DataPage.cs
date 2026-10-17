using CycloneHQ.App.Services;
using Microsoft.AspNetCore.Components;

namespace CycloneHQ.App.Components;

// Base for pages that read the synced league data: loads it once and re-renders when it changes.
public abstract class DataPage : ComponentBase, IDisposable
{
    [Inject] protected LeagueData Data { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Data.Changed += OnDataChanged;
        await Data.EnsureLoadedAsync();
    }

    void OnDataChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => Data.Changed -= OnDataChanged;
}
