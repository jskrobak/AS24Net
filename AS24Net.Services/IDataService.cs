using Havit.Blazor.Components.Web.Bootstrap;
using AS24Net.DataLayer;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.Services;

/// <summary>Reading and saving what the administration pages show.</summary>
public interface IDataService
{
    Task<DataFragment<Partner>> GetPartnersDataFragmentAsync(PartnerFilter filter, GridDataProviderRequest<Partner> request,
        CancellationToken cancellationToken = default);
    Task<DataFragment<Identity>> GetIdentitiesDataFragmentAsync(IdentityFilter filter, GridDataProviderRequest<Identity> request,
        CancellationToken cancellationToken = default);
    Task<DataFragment<Certificate>> GetCertificatesDataFragmentAsync(CertificateFilter filter, GridDataProviderRequest<Certificate> request,
        CancellationToken cancellationToken = default);
    Task<DataFragment<OutgoingMessage>> GetOutgoingMessagesDataFragmentAsync(OutgoingMessageFilter filter,
        GridDataProviderRequest<OutgoingMessage> request, CancellationToken cancellationToken = default);
    Task<DataFragment<ReceivedMessage>> GetReceivedMessagesDataFragmentAsync(ReceivedMessageFilter filter,
        GridDataProviderRequest<ReceivedMessage> request, CancellationToken cancellationToken = default);
    Task<DataFragment<CertificateChange>> GetCertificateChangesDataFragmentAsync(CertificateChangeFilter filter,
        GridDataProviderRequest<CertificateChange> request, CancellationToken cancellationToken = default);

    Task<List<Partner>> GetAllPartnersAsync();
    Task<List<Identity>> GetAllIdentitiesAsync();
    Task<List<Certificate>> GetAllCertificatesAsync();

    /// <param name="originalSignatureCertificateId">
    /// The signature certificate before the edit; when it was replaced, it is kept as the previous one.
    /// </param>
    Task SavePartnerAsync(Partner partner, int? originalSignatureCertificateId = null);
    Task DeletePartnerAsync(Partner partner);
    /// <param name="originalDecryptionCertificateId">
    /// The decryption certificate before the edit; when it was replaced, it is kept as the previous one.
    /// </param>
    Task SaveIdentityAsync(Identity identity, int? originalDecryptionCertificateId = null);
    Task DeleteIdentityAsync(Identity identity);
    Task SaveCertificateAsync(Certificate certificate);

    /// <summary>Deletes a certificate that nothing uses; throws <see cref="InvalidOperationException"/> naming who does.</summary>
    Task DeleteCertificateAsync(Certificate certificate);

    Task<ReceivedMessage?> GetReceivedMessageAsync(int id);
    Task MarkReceivedMessageFetchedAsync(ReceivedMessage message);
}
