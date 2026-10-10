using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Services.TimeServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.ConnectionTests;

namespace AS24Net.Server.Components.Pages;

public partial class ConnectionTests : ComponentBase, IDisposable
{
    [Inject] protected ConnectionTestService Tests { get; set; } = null!;
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected ServerCertificateService ServerCertificates { get; set; } = null!;
    [Inject] protected ITimeService TimeService { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private List<Partner> partners = [];

    private HxModal serverCertificateModal = null!;
    private Partner? serverCertificatePartner;
    private ServerCertificate? serverCertificate;

    /// <summary>The partner whose server certificate is being read.</summary>
    private int? fetching;
    private List<Identity> identities = [];

    /// <summary>Empty: every partner is tested as its default identity.</summary>
    private int? identityId;

    private bool CanStart => !Tests.IsRunning && identities.Count > 0;

    private List<Partner> Failed => partners
        .Where(p => Tests.Results.GetValueOrDefault(p.Id) is { Success: false })
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadPartnersAsync();
        identities = await DataService.GetAllIdentitiesAsync();
        Tests.Changed += HandleChanged;
    }

    private async Task LoadPartnersAsync() =>
        partners = (await DataService.GetAllPartnersAsync()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    #region Sending a test message

    private HxModal testMessageModal = null!;
    private Partner? testMessagePartner;

    private async Task SendTestMessageAsync(Partner partner)
    {
        testMessagePartner = partner;
        await testMessageModal.ShowAsync();
    }

    #endregion

    #region Changing a partner or a connection and testing again

    private HxModal partnerEditModal = null!;
    private HxModal connectionEditModal = null!;
    private Partner? editedPartner;
    private Connection? editedConnection;
    private List<Connection> connections = [];
    private List<Certificate> certificates = [];
    private List<Certificate> ownCertificates => certificates.Where(c => c.HasPrivateKey).ToList();

    private async Task EditPartnerAsync(Partner partner)
    {
        connections = await DataService.GetAllConnectionsAsync();
        certificates = await DataService.GetAllCertificatesAsync();
        editedPartner = partner;
        await partnerEditModal.ShowAsync();
    }

    private async Task EditConnectionAsync(Connection connection)
    {
        certificates = await DataService.GetAllCertificatesAsync();
        editedConnection = connection;
        await connectionEditModal.ShowAsync();
    }

    private async Task HandlePartnerSavedAsync(Partner partner)
    {
        await partnerEditModal.HideAsync();
        await LoadPartnersAsync();
        Start(partners.Where(p => p.Id == partner.Id).ToList());
    }

    /// <summary>The connection is shared: all its partners are tested again.</summary>
    private async Task HandleConnectionSavedAsync(Connection connection)
    {
        await connectionEditModal.HideAsync();
        await LoadPartnersAsync();
        Start(partners.Where(p => p.ConnectionId == connection.Id).ToList());
    }

    #endregion

    private void HandleChanged() => InvokeAsync(StateHasChanged);

    private void Start(IReadOnlyList<Partner> selected)
    {
        try
        {
            if (!Tests.Start(selected.Select(p => p.Id).ToList(), identityId))
                Messenger.AddWarning("Connection tests are running already.");
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
        }
    }

    private DateTime Now => TimeService.GetCurrentTime();

    private static bool IsHttps(Partner partner) =>
        Uri.TryCreate(partner.Connection.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task ShowServerCertificateAsync(Partner partner)
    {
        fetching = partner.Id;
        try
        {
            serverCertificate = await ServerCertificates.FetchAsync(partner.Id);
            serverCertificatePartner = await ServerCertificates.GetPartnerAsync(partner.Id);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }
        finally
        {
            fetching = null;
        }

        await serverCertificateModal.ShowAsync();
    }

    private async Task UseServerCertificateAsync()
    {
        if (serverCertificate is null || serverCertificatePartner is null)
            return;

        try
        {
            var change = await ServerCertificates.UseAsSignatureCertificateAsync(serverCertificatePartner.Id, serverCertificate,
                (await AuthenticationState).User.Identity?.Name);
            // The scheduler applies the change in a moment; a test started at once could still see the old certificate.
            Messenger.AddInformation($"The certificate is applied to connection {change.ConnectionName} right away. Test the partner again in a moment.");
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await serverCertificateModal.HideAsync();
    }

    internal static ThemeColor ResultColor(ConnectionTestResult result) =>
        !result.Success ? ThemeColor.Danger : result.Warnings.Count > 0 ? ThemeColor.Warning : ThemeColor.Success;

    internal static string ResultText(ConnectionTestResult result) => result.Stage switch
    {
        ConnectionTestStage.Completed when result.Problems.Count > 0 => "failed: configuration",
        ConnectionTestStage.Completed when result.Warnings.Count > 0 => "OK, with warnings",
        ConnectionTestStage.Completed => "OK",
        ConnectionTestStage.Setup => "failed: URL",
        ConnectionTestStage.Connect => "failed: connection",
        ConnectionTestStage.Tls => "failed: TLS",
        ConnectionTestStage.Http => "failed: HTTP",
        _ => result.Stage.ToString(),
    };

    public void Dispose() => Tests.Changed -= HandleChanged;
}
