using System.Net;
using Havit.Blazor.Components.Web;
using Havit.Services.TimeServices;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.Certificates;

namespace AS24Net.Server.Components.Pages;

public partial class CertificateChanges : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected CertificateChangeService ChangeService { get; set; } = null!;
    [Inject] protected IUploadService UploadService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected ITimeService TimeService { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    /// <summary>Opens the form for the partner right away (from the partner's menu).</summary>
    [SupplyParameterFromQuery(Name = "partner")] public int? PartnerQuery { get; set; }

    private static readonly PartnerCertificateUsage[] usages = Enum.GetValues<PartnerCertificateUsage>();
    private static readonly CertificateChangeStatus[] statuses = Enum.GetValues<CertificateChangeStatus>();
    private static readonly bool[] sources = [true, false];

    private CertificateChangeFilter filterModel = new();
    private HxGrid<CertificateChange> gridComponent = null!;
    private HxModal scheduleModal = null!;
    private HxInputFile? inputFileComponent;
    private List<Partner> partners = [];
    private List<Certificate> certificates = [];

    private int? partnerId;
    private PartnerCertificateUsage usage = PartnerCertificateUsage.SignatureAndEncryption;
    private DateTime activateAt;
    private bool uploadFile = true;
    private int? certificateId;
    private string? note;

    protected override async Task OnInitializedAsync()
    {
        partners = await DataService.GetAllPartnersAsync();
        certificates = (await DataService.GetAllCertificatesAsync()).Where(c => !c.HasPrivateKey).ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && PartnerQuery is { } partner && partners.Any(p => p.Id == partner))
            await ShowScheduleAsync(partner);
    }

    private async Task<GridDataProviderResult<CertificateChange>> GetGridData(GridDataProviderRequest<CertificateChange> request)
    {
        var response = await DataService.GetCertificateChangesDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<CertificateChange> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task ShowScheduleAsync(int? partner)
    {
        partnerId = partner;
        usage = PartnerCertificateUsage.SignatureAndEncryption;
        // A whole hour, a week ahead: a usual announcement of a partner.
        var now = TimeService.GetCurrentTime();
        activateAt = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddDays(7);
        certificateId = null;
        note = null;
        uploadFile = true;
        await scheduleModal.ShowAsync();
    }

    private async Task HandleScheduleAsync()
    {
        if (partnerId is null)
        {
            Messenger.AddWarning("Select the partner.");
            return;
        }

        if (uploadFile)
        {
            // The file goes to the upload endpoint first; the schedule follows in HandleFileUploaded.
            await inputFileComponent!.StartUploadAsync();
            return;
        }

        if (certificateId is null)
        {
            Messenger.AddWarning("Select the certificate.");
            return;
        }

        await ScheduleAsync(() => ChangeService.ScheduleAsync(partnerId.Value, certificateId.Value, usage, activateAt, note, UserName()));
    }

    private async Task HandleFileUploaded(FileUploadedEventArgs uploaded)
    {
        if (uploaded is not { ResponseStatus: HttpStatusCode.OK })
        {
            Messenger.AddError($"Upload failed: HTTP {uploaded.ResponseStatus}.");
            return;
        }

        var data = await UploadService.ReadAllBytesAsync(uploaded.ResponseText.Replace("\"", ""));
        await ScheduleAsync(() => ChangeService.ScheduleFileAsync(partnerId!.Value, data, uploaded.OriginalFileName, usage, activateAt, note, UserName()));
    }

    private async Task ScheduleAsync(Func<Task<CertificateChange>> schedule)
    {
        try
        {
            var change = await schedule();
            Messenger.AddInformation(change.ActivateAt <= TimeService.GetCurrentTime()
                ? "The certificate is applied right away."
                : $"The certificate is used from {change.ActivateAt:g}.");
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await scheduleModal.HideAsync();
        certificates = (await DataService.GetAllCertificatesAsync()).Where(c => !c.HasPrivateKey).ToList();
        await gridComponent.RefreshDataAsync();
    }

    private async Task HandleCancelAsync(CertificateChange change)
    {
        try
        {
            await ChangeService.CancelAsync(change.Id, UserName());
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
        }

        await gridComponent.RefreshDataAsync();
    }

    private string? UserName() => AuthenticationState.Result.User.Identity?.Name;

    private static ThemeColor StatusColor(CertificateChangeStatus status) => status switch
    {
        CertificateChangeStatus.Scheduled => ThemeColor.Info,
        CertificateChangeStatus.Applied => ThemeColor.Success,
        CertificateChangeStatus.Failed => ThemeColor.Danger,
        _ => ThemeColor.Secondary,
    };
}
