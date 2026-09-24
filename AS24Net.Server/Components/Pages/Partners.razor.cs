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

    private static readonly MdnMode[] mdnModes = Enum.GetValues<MdnMode>();

    private Partner? currentPartner;
    private int? originalSignatureCertificateId;
    private PartnerFilter filterModel = new();
    private HxGrid<Partner> gridComponent = null!;
    private HxModal partnerEditModal = null!;
    private List<Certificate> allCertificates = [];
    private List<Certificate> certificates = [];
    private List<Identity> identities = [];

    protected override async Task OnInitializedAsync()
    {
        allCertificates = await DataService.GetAllCertificatesAsync();
        identities = await DataService.GetAllIdentitiesAsync();
    }

    /// <summary>
    /// The partner's certificates are the ones without a private key (a CA certificate for HTTPS is one of them too),
    /// and those the partner uses already, e.g. a certificate shared with our identity: the selects need their items.
    /// </summary>
    private void PrepareCertificates(Partner partner)
    {
        int?[] used = [partner.SignatureCertificateId, partner.EncryptionCertificateId, partner.TlsCertificateId];
        certificates = allCertificates.Where(c => !c.HasPrivateKey || used.Contains(c.Id)).ToList();
    }

    private static string CertificateText(Certificate c) => $"{c.Name} (valid to {c.ValidTo:d})";

    private string? CertificateValidity(int? id) =>
        id is null ? null : certificates.FirstOrDefault(c => c.Id == id) is { } c ? $"valid to {c.ValidTo:d}" : null;

    private async Task<GridDataProviderResult<Partner>> GetGridData(GridDataProviderRequest<Partner> request)
    {
        var response = await DataService.GetPartnersDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<Partner> { Data = response.Data, TotalCount = response.TotalCount };
    }

    private async Task HandleNewItemClicked()
    {
        currentPartner = new Partner { DefaultIdentityId = identities.Count == 1 ? identities[0].Id : null };
        originalSignatureCertificateId = null;
        PrepareCertificates(currentPartner);
        await partnerEditModal.ShowAsync();
    }

    private async Task HandleSelectedAsync(Partner? partner)
    {
        if (partner is null)
            return;

        currentPartner = partner;
        originalSignatureCertificateId = partner.SignatureCertificateId;
        PrepareCertificates(partner);
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
            await DataService.SavePartnerAsync(currentPartner!, originalSignatureCertificateId);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await gridComponent.RefreshDataAsync();
        await partnerEditModal.HideAsync();
    }

    #region Testing the connection to one partner

    private HxModal connectionTestModal = null!;
    private Partner? connectionTestPartner;

    private async Task HandleTestConnectionClick(Partner partner)
    {
        connectionTestPartner = partner;
        await connectionTestModal.ShowAsync();
    }

    #endregion
}
