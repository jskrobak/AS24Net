using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using AS24Net.Core.As2;
using AS24Net.Core.Security;
using AS24Net.DataLayer;
using AS24Net.DataLayer.Filters;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.As2;

namespace AS24Net.Services;

public class DataService(
    IConnectionRepository connectionRepository,
    IPartnerRepository partnerRepository,
    IIdentityRepository identityRepository,
    ICertificateRepository certificateRepository,
    IOutgoingMessageRepository outgoingRepository,
    IReceivedMessageRepository receivedRepository,
    ICertificateChangeRepository changeRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    As2HttpClientProvider httpClients) : IDataService
{
    public Task<DataFragment<Connection>> GetConnectionsDataFragmentAsync(ConnectionFilter filter, GridDataProviderRequest<Connection> request,
        CancellationToken cancellationToken = default) => connectionRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<Partner>> GetPartnersDataFragmentAsync(PartnerFilter filter, GridDataProviderRequest<Partner> request,
        CancellationToken cancellationToken = default) => partnerRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<Identity>> GetIdentitiesDataFragmentAsync(IdentityFilter filter, GridDataProviderRequest<Identity> request,
        CancellationToken cancellationToken = default) => identityRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<Certificate>> GetCertificatesDataFragmentAsync(CertificateFilter filter, GridDataProviderRequest<Certificate> request,
        CancellationToken cancellationToken = default) => certificateRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<OutgoingMessage>> GetOutgoingMessagesDataFragmentAsync(OutgoingMessageFilter filter,
        GridDataProviderRequest<OutgoingMessage> request, CancellationToken cancellationToken = default) =>
        outgoingRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<ReceivedMessage>> GetReceivedMessagesDataFragmentAsync(ReceivedMessageFilter filter,
        GridDataProviderRequest<ReceivedMessage> request, CancellationToken cancellationToken = default) =>
        receivedRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<DataFragment<CertificateChange>> GetCertificateChangesDataFragmentAsync(CertificateChangeFilter filter,
        GridDataProviderRequest<CertificateChange> request, CancellationToken cancellationToken = default) =>
        changeRepository.GetFragmentAsync(filter, request, cancellationToken);

    public Task<List<Connection>> GetAllConnectionsAsync() => connectionRepository.GetAllWithPartnersAsync();

    public Task<List<Partner>> GetAllPartnersAsync() => partnerRepository.GetAllWithConnectionAsync();

    public async Task<List<Identity>> GetAllIdentitiesAsync() => (await identityRepository.GetAllAsync()).OrderBy(i => i.Name).ToList();

    public async Task<List<Certificate>> GetAllCertificatesAsync() => (await certificateRepository.GetAllAsync()).OrderBy(c => c.Name).ToList();

    public async Task SaveConnectionAsync(Connection connection, int? originalSignatureCertificateId = null)
    {
        connection.Name = connection.Name.Trim();
        if (connection.Name.Length == 0)
            throw new InvalidOperationException("The connection needs a name.");
        connection.Url = connection.Url.Trim();
        if (!Uri.TryCreate(connection.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The URL of the connection must be an absolute http or https address.");
        if ((await connectionRepository.GetAllAsync()).Any(c => c.Id != connection.Id && c.Name == connection.Name))
            throw new InvalidOperationException($"Another connection has the name {connection.Name}.");
        if (!As2Algorithms.DigestAlgorithms.Contains(connection.SignatureAlgorithm))
            throw new InvalidOperationException($"The signature digest must be one of {string.Join(", ", As2Algorithms.DigestAlgorithms)}.");
        if (!As2Algorithms.EncryptionAlgorithms.Contains(connection.EncryptionAlgorithm))
            throw new InvalidOperationException($"The encryption must be one of {string.Join(", ", As2Algorithms.EncryptionAlgorithms)}.");
        if (connection.EncryptMessages && connection.EncryptionCertificateId is null)
            throw new InvalidOperationException("Encrypting messages needs the encryption certificate of the partner.");
        if (connection.RequireSignedMessages && connection.SignatureCertificateId is null)
            throw new InvalidOperationException("Requiring signed messages needs the signature certificate of the partner.");

        // The signature certificate being replaced is kept: messages signed with it may still be on their way.
        if (originalSignatureCertificateId is { } original && original != connection.SignatureCertificateId)
            connection.PreviousSignatureCertificateId = original;

        if (connection.Id == 0)
            unitOfWork.AddForInsert(connection);
        else
            unitOfWork.AddForUpdate(connection);
        await unitOfWork.CommitAsync();

        // The trusted HTTPS certificate may have changed.
        httpClients.Reset();
    }

    public async Task DeleteConnectionAsync(Connection connection)
    {
        var partners = (await partnerRepository.GetAllAsync()).Where(p => p.ConnectionId == connection.Id).Select(p => p.Name).ToList();
        if (partners.Count > 0)
            throw new InvalidOperationException($"Connection {connection.Name} is used by partner(s) {string.Join(", ", partners)}.");

        unitOfWork.AddForDelete(connection);
        await unitOfWork.CommitAsync();
    }

    public async Task SavePartnerAsync(Partner partner)
    {
        partner.As2Id = partner.As2Id.Trim();
        if (!As2Headers.IsValidAs2Id(partner.As2Id))
            throw new InvalidOperationException("The AS2 name must have 1 to 128 printable ASCII characters.");
        if (partner.ConnectionId == 0)
            throw new InvalidOperationException("Select the connection the partner is reached through.");
        if ((await partnerRepository.GetAllAsync()).Any(p => p.Id != partner.Id && p.As2Id == partner.As2Id))
            throw new InvalidOperationException($"Another partner has the AS2 name {partner.As2Id}.");

        if (partner.Id == 0)
            unitOfWork.AddForInsert(partner);
        else
            unitOfWork.AddForUpdate(partner);
        await unitOfWork.CommitAsync();
    }

    public async Task DeletePartnerAsync(Partner partner)
    {
        unitOfWork.AddForDelete(partner);
        await unitOfWork.CommitAsync();
    }

    public async Task SaveIdentityAsync(Identity identity, int? originalDecryptionCertificateId = null)
    {
        identity.As2Id = identity.As2Id.Trim();
        if (!As2Headers.IsValidAs2Id(identity.As2Id))
            throw new InvalidOperationException("The AS2 name must have 1 to 128 printable ASCII characters.");
        if ((await identityRepository.GetAllAsync()).Any(i => i.Id != identity.Id && i.As2Id == identity.As2Id))
            throw new InvalidOperationException($"Another identity has the AS2 name {identity.As2Id}.");

        // Keep the decryption certificate being replaced: partners may still encrypt for it for a while.
        if (originalDecryptionCertificateId is { } original && original != identity.DecryptionCertificateId)
            identity.PreviousDecryptionCertificateId = original;

        if (identity.Id == 0)
            unitOfWork.AddForInsert(identity);
        else
            unitOfWork.AddForUpdate(identity);

        await unitOfWork.CommitAsync();
    }

    public async Task DeleteIdentityAsync(Identity identity)
    {
        unitOfWork.AddForDelete(identity);
        await unitOfWork.CommitAsync();
    }

    public async Task SaveCertificateAsync(Certificate certificate)
    {
        if (certificate.Id == 0)
        {
            certificate.Created = timeService.GetCurrentTime();
            unitOfWork.AddForInsert(certificate);
        }
        else
        {
            unitOfWork.AddForUpdate(certificate);
        }

        await unitOfWork.CommitAsync();
    }

    public async Task DeleteCertificateAsync(Certificate certificate)
    {
        var id = certificate.Id;
        var users = (await identityRepository.GetAllAsync())
            .Where(i => i.SigningCertificateId == id || i.DecryptionCertificateId == id || i.PreviousDecryptionCertificateId == id)
            .Select(i => $"identity {i.Name}")
            .Concat((await connectionRepository.GetAllAsync())
                .Where(c => c.SignatureCertificateId == id || c.PreviousSignatureCertificateId == id
                                                           || c.EncryptionCertificateId == id || c.TlsCertificateId == id)
                .Select(c => $"connection {c.Name}"))
            .Concat((await changeRepository.GetScheduledAsync())
                .Where(c => c.CertificateId == id)
                .Select(c => $"the change of connection {c.ConnectionName} at {c.ActivateAt:g}"))
            .ToList();
        if (users.Count > 0)
            throw new InvalidOperationException($"Certificate {certificate.Name} is used by {string.Join(", ", users)}.");

        unitOfWork.AddForDelete(certificate);
        await unitOfWork.CommitAsync();
    }

    public Task<ReceivedMessage?> GetReceivedMessageAsync(int id) => receivedRepository.FindWithRefsAsync(id);

    public async Task MarkReceivedMessageFetchedAsync(ReceivedMessage message)
    {
        message.FetchedDate = timeService.GetCurrentTime();
        unitOfWork.AddForUpdate(message);
        await unitOfWork.CommitAsync();
    }
}
