using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class Partners : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;

    private Partner? currentPartner;
    private PartnerFilter filterModel = new();
    private HxGrid<Partner> gridComponent = null!;
    private HxModal partnerEditModal = null!;
    private List<Connection> connections = [];
    private List<Identity> identities = [];

    /// <summary>Opens the form of a new partner of the connection right away (from the connection's menu).</summary>
    [SupplyParameterFromQuery(Name = "connection")] public int? ConnectionQuery { get; set; }

    protected override async Task OnInitializedAsync()
    {
        connections = await DataService.GetAllConnectionsAsync();
        identities = await DataService.GetAllIdentitiesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && ConnectionQuery is { } connection && connections.Any(c => c.Id == connection))
            await ShowNewAsync(connection);
    }

    private async Task<GridDataProviderResult<Partner>> GetGridData(GridDataProviderRequest<Partner> request)
    {
        var response = await DataService.GetPartnersDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<Partner> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private Task HandleNewItemClicked() => ShowNewAsync(null);

    private async Task ShowNewAsync(int? connectionId)
    {
        if (connections.Count == 0)
        {
            Messenger.AddWarning("Set up the connection of the partner in Connections first.");
            return;
        }

        currentPartner = new Partner
        {
            ConnectionId = connectionId ?? connections[0].Id,
            DefaultIdentityId = identities.Count == 1 ? identities[0].Id : null,
        };
        await partnerEditModal.ShowAsync();
    }

    private async Task HandleSelectedAsync(Partner? partner)
    {
        if (partner is null)
            return;

        currentPartner = partner;
        await partnerEditModal.ShowAsync();
    }

    private async Task HandleDeleteClick(Partner partner)
    {
        try
        {
            await DataService.DeletePartnerAsync(partner);
        }
        catch (Exception ex)
        {
            Messenger.AddError($"Delete failed: {ex.Message}");
        }

        await gridComponent.RefreshDataAsync();
    }

    private async Task SavePartner()
    {
        try
        {
            await DataService.SavePartnerAsync(currentPartner!);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await gridComponent.RefreshDataAsync();
        await partnerEditModal.HideAsync();
    }
}
