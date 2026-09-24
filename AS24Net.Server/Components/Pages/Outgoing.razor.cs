using System.Net;
using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.As2;
using AS24Net.Services.Storage;

namespace AS24Net.Server.Components.Pages;

public partial class Outgoing : ComponentBase, IDisposable
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected MessageQueueService Queue { get; set; } = null!;
    [Inject] protected As2SendService SendService { get; set; } = null!;
    [Inject] protected OutboxStorage Outbox { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected IConfiguration Configuration { get; set; } = null!;

    /// <summary>Opens the form for the partner right away (from the partner's menu).</summary>
    [SupplyParameterFromQuery(Name = "partner")] public int? PartnerQuery { get; set; }

    private static readonly OutgoingStatus[] statuses = Enum.GetValues<OutgoingStatus>();

    private OutgoingMessageFilter filterModel = new();
    private HxGrid<OutgoingMessage> gridComponent = null!;
    private HxModal newModal = null!;
    private HxModal detailModal = null!;
    private HxInputFile inputFileComponent = null!;
    private OutgoingMessage? selected;
    private List<Partner> partners = [];
    private List<Identity> identities = [];

    private string? filePath;
    private string? fileName;
    private int? partnerId;
    private int? identityId;
    private string? contentType;
    private string? subject;
    private float? uploadProgress;
    private bool queued;

    private long MaxUploadFileSize => Configuration.GetValue("Upload:MaxOutboxFileSizeMB", 512L) * 1024 * 1024;

    protected override async Task OnInitializedAsync()
    {
        partners = await DataService.GetAllPartnersAsync();
        identities = await DataService.GetAllIdentitiesAsync();
        SendService.Changed += HandleSendServiceChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && PartnerQuery is { } partner && partners.Any(p => p.Id == partner))
            await ShowNewAsync(partner);
    }

    private DateTime lastRefresh;

    /// <summary>The list follows the transfers, at most every few seconds.</summary>
    private void HandleSendServiceChanged()
    {
        if (DateTime.Now - lastRefresh < TimeSpan.FromSeconds(3))
            return;

        lastRefresh = DateTime.Now;
        InvokeAsync(() => gridComponent.RefreshDataAsync());
    }

    private async Task<GridDataProviderResult<OutgoingMessage>> GetGridData(GridDataProviderRequest<OutgoingMessage> request)
    {
        var response = await DataService.GetOutgoingMessagesDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<OutgoingMessage> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task ShowNewAsync(int? partner)
    {
        filePath = null;
        fileName = null;
        partnerId = partner;
        identityId = null;
        subject = null;
        uploadProgress = null;
        queued = false;
        HandlePartnerChanged();
        await newModal.ShowAsync();
    }

    private void HandlePartnerChanged() => contentType = partners.FirstOrDefault(p => p.Id == partnerId)?.ContentType ?? contentType;

    private async Task HandleFileSelected(InputFileChangeEventArgs args)
    {
        if (args.FileCount == 0)
            return;

        // The file is uploaded to the outbox right away; the path is filled in when it is there.
        uploadProgress = 0;
        await inputFileComponent.StartUploadAsync();
    }

    private void HandleUploadProgress(UploadProgressEventArgs progress) =>
        uploadProgress = progress.UploadedBytes * 100f / Math.Max(1, progress.UploadSize);

    private void HandleFileUploaded(FileUploadedEventArgs uploaded)
    {
        uploadProgress = null;
        if (uploaded.ResponseStatus != HttpStatusCode.OK)
        {
            Messenger.AddError(uploaded.ResponseStatus == HttpStatusCode.RequestEntityTooLarge
                ? $"The file is larger than {MaxUploadFileSize / 1024 / 1024} MB."
                : $"The upload of {uploaded.OriginalFileName} failed ({(int)uploaded.ResponseStatus}): {uploaded.ResponseText}");
            return;
        }

        filePath = uploaded.ResponseText.Trim('"');
        fileName = uploaded.OriginalFileName;
    }

    private async Task HandleQueueAsync()
    {
        if (partnerId is null || string.IsNullOrWhiteSpace(filePath))
        {
            Messenger.AddWarning("Select the file and the partner.");
            return;
        }

        try
        {
            var message = await Queue.QueueAsync(new QueueRequest
            {
                PartnerId = partnerId.Value,
                IdentityId = identityId,
                FilePath = filePath,
                FileName = fileName,
                ContentType = contentType,
                Subject = subject,
            });
            Messenger.AddInformation($"{message.FileName} is queued for {message.Partner.Name}.");
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        queued = true;
        await newModal.HideAsync();
        await gridComponent.RefreshDataAsync();
    }

    /// <summary>A file uploaded to the outbox and then not queued is deleted again.</summary>
    private async Task HandleNewClosed()
    {
        if (!queued && filePath is not null)
            await Outbox.DeleteIfInOutboxAsync(filePath);
        filePath = null;
    }

    private void HandleSendNow()
    {
        SendService.Trigger();
        Messenger.AddInformation("The send queue is processed now.");
    }

    private async Task HandleSelectedAsync(OutgoingMessage? message)
    {
        selected = message;
        if (selected is not null)
            await detailModal.ShowAsync();
    }

    private async Task HandleRequeueAsync(OutgoingMessage message)
    {
        await Queue.RequeueAsync(message.Id);
        await detailModal.HideAsync();
        await gridComponent.RefreshDataAsync();
    }

    private async Task HandleDeleteAsync(OutgoingMessage message)
    {
        if (message.Status == OutgoingStatus.New && SendService.ActiveTransfers > 0)
            Messenger.AddWarning("The message may be being sent at the moment.");

        await Queue.DeleteAsync(message.Id);
        await gridComponent.RefreshDataAsync();
    }

    private static string Security(OutgoingMessage m)
    {
        var parts = new List<string>();
        if (m.Signed) parts.Add("signed");
        if (m.Encrypted) parts.Add("encrypted");
        if (m.Compressed) parts.Add("compressed");
        return m.SentDate is null ? "" : parts.Count == 0 ? "unsecured" : string.Join(", ", parts);
    }

    private static ThemeColor StatusColor(OutgoingStatus status) => status switch
    {
        OutgoingStatus.Delivered => ThemeColor.Success,
        OutgoingStatus.Sent => ThemeColor.Info,
        OutgoingStatus.Error => ThemeColor.Warning,
        OutgoingStatus.Failed or OutgoingStatus.NotDelivered => ThemeColor.Danger,
        _ => ThemeColor.Secondary,
    };

    public void Dispose() => SendService.Changed -= HandleSendServiceChanged;
}
