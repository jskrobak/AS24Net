using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class Identities : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;

    private Identity? currentIdentity;
    private int? originalDecryptionCertificateId;
    private IdentityFilter filterModel = new();
    private HxGrid<Identity> gridComponent = null!;
    private HxModal identityEditModal = null!;

    /// <summary>Our certificates, i.e. the ones with a private key.</summary>
    private List<Certificate> ownCertificates = [];

    protected override async Task OnInitializedAsync() =>
        ownCertificates = (await DataService.GetAllCertificatesAsync()).Where(c => c.HasPrivateKey).ToList();

    private string? CertificateName(int? id) => id is null ? null : ownCertificates.FirstOrDefault(c => c.Id == id)?.Name;

    private async Task<GridDataProviderResult<Identity>> GetGridData(GridDataProviderRequest<Identity> request)
    {
        var response = await DataService.GetIdentitiesDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<Identity> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task HandleNewItemClicked()
    {
        // A new identity uses our only certificate right away.
        var only = ownCertificates.Count == 1 ? ownCertificates[0].Id : (int?)null;
        currentIdentity = new Identity { SigningCertificateId = only, DecryptionCertificateId = only };
        originalDecryptionCertificateId = null;
        await identityEditModal.ShowAsync();
    }

    private async Task HandleSelectedAsync(Identity? identity)
    {
        // Clicking a selected row deselects it.
        if (identity is null)
            return;

        currentIdentity = identity;
        originalDecryptionCertificateId = identity.DecryptionCertificateId;
        await identityEditModal.ShowAsync();
    }

    private async Task HandleDeleteClick(Identity identity)
    {
        try
        {
            await DataService.DeleteIdentityAsync(identity);
        }
        catch (Exception ex)
        {
            Messenger.AddError($"Delete failed: {ex.Message}");
        }

        await gridComponent.RefreshDataAsync();
    }

    #region One certificate for all identities

    private HxModal allCertificateModal = null!;
    private int? allCertificateId;
    private bool allSigning = true;
    private bool allDecryption = true;

    private async Task SetCertificateForAllAsync()
    {
        try
        {
            var changed = await DataService.SetCertificateForAllIdentitiesAsync(allCertificateId!.Value, allSigning, allDecryption);
            Messenger.AddInformation(changed == 0
                ? "All identities have the certificate already."
                : $"The certificate is set for {changed} identit{(changed == 1 ? "y" : "ies")}.");
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await gridComponent.RefreshDataAsync();
        await allCertificateModal.HideAsync();
    }

    #endregion

    private async Task SaveIdentity()
    {
        try
        {
            await DataService.SaveIdentityAsync(currentIdentity!, originalDecryptionCertificateId);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await gridComponent.RefreshDataAsync();
        await identityEditModal.HideAsync();
    }
}
