using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class Connections : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;

    private static readonly MdnMode[] mdnModes = Enum.GetValues<MdnMode>();

    private Connection? currentConnection;
    private int? originalSignatureCertificateId;
    private ConnectionFilter filterModel = new();
    private HxGrid<Connection> gridComponent = null!;
    private HxModal connectionEditModal = null!;
    private List<Certificate> certificates = [];

    protected override async Task OnInitializedAsync()
    {
        // The partner's certificates are the ones without a private key; a CA certificate for HTTPS is one of them too.
        certificates = (await DataService.GetAllCertificatesAsync()).Where(c => !c.HasPrivateKey).ToList();
    }

    private static string CertificateText(Certificate c) => $"{c.Name} (valid to {c.ValidTo:d})";

    private string? CertificateValidity(int? id) =>
        id is null ? null : certificates.FirstOrDefault(c => c.Id == id) is { } c ? $"valid to {c.ValidTo:d}" : null;

    private async Task<GridDataProviderResult<Connection>> GetGridData(GridDataProviderRequest<Connection> request)
    {
        var response = await DataService.GetConnectionsDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<Connection> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task HandleNewItemClicked()
    {
        currentConnection = new Connection();
        originalSignatureCertificateId = null;
        await connectionEditModal.ShowAsync();
    }

    private async Task HandleSelectedAsync(Connection? connection)
    {
        if (connection is null)
            return;

        currentConnection = connection;
        originalSignatureCertificateId = connection.SignatureCertificateId;
        await connectionEditModal.ShowAsync();
    }

    private async Task HandleDeleteClick(Connection connection)
    {
        try
        {
            await DataService.DeleteConnectionAsync(connection);
        }
        catch (Exception ex)
        {
            Messenger.AddError($"Delete failed: {ex.Message}");
        }

        await gridComponent.RefreshDataAsync();
    }

    private async Task SaveConnection()
    {
        try
        {
            await DataService.SaveConnectionAsync(currentConnection!, originalSignatureCertificateId);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await gridComponent.RefreshDataAsync();
        await connectionEditModal.HideAsync();
    }
}
