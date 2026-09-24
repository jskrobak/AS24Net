using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.Services;
using AS24Net.Services.As2;
using AS24Net.Services.Retention;

namespace AS24Net.Server.Components.Pages;

public partial class Settings : ComponentBase, IDisposable
{
    [Inject] protected RetentionService Retention { get; set; } = null!;
    [Inject] protected GlobalSettingsService GlobalSettingsService { get; set; } = null!;
    [Inject] protected As2SendService SendService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;

    private Services.GlobalSettings? settings;

    protected override async Task OnInitializedAsync()
    {
        settings = await GlobalSettingsService.GetGlobalSettingsAsync();
        Retention.Changed += HandleRetentionChanged;
    }

    private void HandleRetentionChanged() => InvokeAsync(StateHasChanged);

    private async Task RunRetentionAsync()
    {
        var result = await Retention.RunAsync();
        if (result.Error is null)
            Messenger.AddInformation($"Clean up finished: {result}.");
        else
            Messenger.AddWarning($"Clean up finished with an error: {result.Error}");
    }

    private async Task SaveAsync()
    {
        settings!.PublicUrl = string.IsNullOrWhiteSpace(settings.PublicUrl) ? null : settings.PublicUrl.Trim();
        await GlobalSettingsService.SetGlobalSettingsAsync(settings);
        // Apply a changed interval immediately.
        SendService.Trigger();
        Messenger.AddInformation("Settings saved.");
    }

    public void Dispose() => Retention.Changed -= HandleRetentionChanged;
}
