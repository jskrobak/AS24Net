using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AS24Net.DataLayer.Filters;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.As2;
using AS24Net.Services.Health;

namespace AS24Net.Server.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    [Inject] protected As2SendService SendService { get; set; } = null!;
    [Inject] protected HealthMonitor HealthMonitor { get; set; } = null!;
    [Inject] protected HealthCheckService HealthCheckService { get; set; } = null!;
    [Inject] protected GlobalSettingsService SettingsService { get; set; } = null!;
    [Inject] protected IOutgoingMessageRepository Outgoing { get; set; } = null!;
    [Inject] protected IReceivedMessageRepository Received { get; set; } = null!;
    [Inject] protected ICertificateChangeRepository CertificateChanges { get; set; } = null!;

    private int waiting, retrying, awaitingMdn, failed, toFetch;
    private string? publicUrl;
    private List<CertificateChange> scheduled = [];

    protected override async Task OnInitializedAsync()
    {
        SendService.Changed += HandleSendServiceChanged;
        HealthMonitor.Updated += HandleStatusChanged;
        publicUrl = (await SettingsService.GetGlobalSettingsAsync()).PublicUrl;
        scheduled = await CertificateChanges.GetScheduledAsync();
        await LoadCountsAsync();
    }

    private async Task LoadCountsAsync()
    {
        waiting = await CountAsync(OutgoingStatus.New);
        retrying = await CountAsync(OutgoingStatus.Error);
        awaitingMdn = await CountAsync(OutgoingStatus.Sent);
        failed = await CountAsync(OutgoingStatus.Failed) + await CountAsync(OutgoingStatus.NotDelivered);
        toFetch = (await Received.GetListAsync(new ReceivedMessageFilter { OnlyNotFetched = true }, 0, 1)).TotalCount;
    }

    private async Task<int> CountAsync(OutgoingStatus status) =>
        (await Outgoing.GetListAsync(new OutgoingMessageFilter { Status = status }, 0, 1)).TotalCount;

    private DateTime lastRefresh;

    private void HandleSendServiceChanged()
    {
        // The counts are read at most every few seconds, the send service reports every transfer.
        if (DateTime.Now - lastRefresh < TimeSpan.FromSeconds(3))
        {
            HandleStatusChanged();
            return;
        }

        lastRefresh = DateTime.Now;
        InvokeAsync(async () =>
        {
            try
            {
                await LoadCountsAsync();
            }
            catch (InvalidOperationException)
            {
                // The context of the page is busy with another query; the next change reads the counts.
            }

            StateHasChanged();
        });
    }

    private void HandleStatusChanged() => InvokeAsync(StateHasChanged);

    private async Task ToggleSendService()
    {
        if (SendService.IsPaused)
            SendService.Resume();
        else
            SendService.Pause();

        // The send service is degraded while it is paused; show it right away.
        await CheckHealthAsync();
    }

    private Task CheckHealthAsync() => HealthMonitor.CheckNowAsync(HealthCheckService);

    private static ThemeColor StatusColor(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => ThemeColor.Success,
        HealthStatus.Degraded => ThemeColor.Warning,
        _ => ThemeColor.Danger,
    };

    public void Dispose()
    {
        SendService.Changed -= HandleSendServiceChanged;
        HealthMonitor.Updated -= HandleStatusChanged;
    }
}
