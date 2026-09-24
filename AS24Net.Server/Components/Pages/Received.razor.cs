using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class Received : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;

    private static readonly ReceivedStatus[] statuses = Enum.GetValues<ReceivedStatus>();

    private ReceivedMessageFilter filterModel = new();
    private HxGrid<ReceivedMessage> gridComponent = null!;
    private HxModal detailModal = null!;
    private ReceivedMessage? selected;

    private async Task<GridDataProviderResult<ReceivedMessage>> GetGridData(GridDataProviderRequest<ReceivedMessage> request)
    {
        var response = await DataService.GetReceivedMessagesDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<ReceivedMessage> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task HandleSelectedAsync(ReceivedMessage? message)
    {
        selected = message;
        if (selected is not null)
            await detailModal.ShowAsync();
    }

    private static string Security(ReceivedMessage m)
    {
        var parts = new List<string>();
        if (m.Signed) parts.Add("signed");
        if (m.Encrypted) parts.Add("encrypted");
        if (m.Compressed) parts.Add("compressed");
        return parts.Count == 0 ? "unsecured" : string.Join(", ", parts);
    }

    private static string MdnText(ReceivedMessage m) => m.MdnStatus switch
    {
        MdnDeliveryStatus.NotRequested => "not requested",
        MdnDeliveryStatus.SentSync => $"returned in the response{(m.MdnSignedRequested ? ", signed" : "")}",
        MdnDeliveryStatus.SentAsync => $"posted at {m.MdnSentDate:G}{(m.MdnSignedRequested ? ", signed" : "")}",
        MdnDeliveryStatus.Retrying => $"posting failed {m.MdnRetryCount} time(s), next attempt at {m.MdnNextRetry:G}",
        MdnDeliveryStatus.Failed => $"could not be posted ({m.MdnRetryCount} attempts)",
        _ => "waiting to be posted",
    };

    private static ThemeColor StatusColor(ReceivedStatus status) => status switch
    {
        ReceivedStatus.Received => ThemeColor.Success,
        ReceivedStatus.Duplicate => ThemeColor.Warning,
        _ => ThemeColor.Danger,
    };
}
