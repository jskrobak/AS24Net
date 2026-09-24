using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.Certificates;

namespace AS24Net.Server.Api;

/// <summary>
/// Connections, partners, identities and certificates through the REST API, e.g. for an import from another AS2
/// server. Reading the connections is open to every token; the rest only to tokens that may change the configuration.
/// The PUT endpoints create the record or update the one with the same key, so an import can be run again; a
/// property that is left out (or null) keeps its value.
/// </summary>
public static class ConfigurationEndpoints
{
    public static void MapConfiguration(this RouteGroupBuilder api)
    {
        api.MapGet("/connections", async (IConnectionRepository repository, CancellationToken cancellationToken) =>
                Results.Ok((await repository.GetAllWithPartnersAsync(cancellationToken)).Select(ConnectionDto.From).ToList()))
            .WithSummary("Lists the connections: the servers of the partners with their security settings and certificates.");

        var configuration = api.MapGroup("")
            .RequireAuthorization(ApiTokenAuthenticationHandler.ConfigurationPolicy);

        configuration.MapGet("/certificates", async (ICertificateRepository repository, CancellationToken cancellationToken) =>
                Results.Ok((await repository.GetAllAsync(cancellationToken)).OrderBy(c => c.Name).Select(CertificateDto.From).ToList()))
            .WithSummary("Lists the stored certificates, without their data.");

        configuration.MapPost("/certificates", async (CertificateRequest request, ICertificateRepository repository,
                IDataService dataService, CancellationToken cancellationToken) =>
            {
                Certificate certificate;
                try
                {
                    certificate = CertificateLoader.CreateEntity(Convert.FromBase64String(request.Data), request.FileName, request.Password);
                }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
                {
                    return Results.BadRequest(new ApiError($"{request.FileName} is not a certificate: {ex.Message}"));
                }

                // The same certificate is stored once, whoever uses it.
                var existing = (await repository.GetAllAsync(cancellationToken))
                    .FirstOrDefault(c => c.Thumbprint == certificate.Thumbprint && c.HasPrivateKey == certificate.HasPrivateKey);
                if (existing is not null)
                    return Results.Ok(CertificateDto.From(existing));

                if (!string.IsNullOrWhiteSpace(request.Name))
                    certificate.Name = request.Name.Trim();
                await dataService.SaveCertificateAsync(certificate);
                return Results.Created($"/api/v1/certificates/{certificate.Id}", CertificateDto.From(certificate));
            })
            .WithSummary("Stores a certificate: PKCS#12 (.pfx, .p12, with the password) or DER / PEM (.cer, .crt, .pem), in base64.")
            .WithDescription("A certificate stored already (the same thumbprint, with or without the private key) is returned instead.");

        configuration.MapPut("/connections/{name}", async (string name, ConnectionRequest request, IConnectionRepository repository,
                IDataService dataService, CancellationToken cancellationToken) =>
            {
                var connection = await ApiEndpoints.FindConnectionAsync(name, repository, cancellationToken);
                var created = connection is null;
                connection ??= new Connection { Name = name.Trim() };
                var originalSignatureCertificateId = connection.SignatureCertificateId;

                if (created && string.IsNullOrWhiteSpace(request.Url))
                    return Results.BadRequest(new ApiError("A new connection needs the URL."));
                if (request.MdnMode is { } mdnMode && !Enum.TryParse<MdnMode>(mdnMode, ignoreCase: true, out _))
                    return Results.BadRequest(new ApiError($"'{mdnMode}' is not an MDN mode: None, Sync or Async."));

                request.ApplyTo(connection);
                try
                {
                    await dataService.SaveConnectionAsync(connection, created ? null : originalSignatureCertificateId);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ApiError(ex.Message));
                }

                var saved = (await repository.GetAllWithPartnersAsync(cancellationToken)).First(c => c.Id == connection.Id);
                return created
                    ? Results.Created($"/api/v1/connections/{Uri.EscapeDataString(saved.Name)}", ConnectionDto.From(saved))
                    : Results.Ok(ConnectionDto.From(saved));
            })
            .WithSummary("Creates the connection with the name or updates it.")
            .WithDescription("Certificates are given by the id from POST /certificates. When the signature certificate is replaced, the " +
                             "previous one is still accepted, as when it is changed in the administration. mdnMode: None, Sync or Async.");

        configuration.MapPut("/partners/{as2Id}", async (string as2Id, PartnerRequest request, IPartnerRepository partners,
                IConnectionRepository connections, IIdentityRepository identities, IDataService dataService,
                CancellationToken cancellationToken) =>
            {
                var partner = (await partners.GetAllWithConnectionAsync(cancellationToken)).FirstOrDefault(p => ApiEndpoints.Matches(p.As2Id, as2Id));
                var created = partner is null;
                partner ??= new Partner { As2Id = as2Id.Trim(), Name = as2Id.Trim() };

                if (request.Connection is { } connectionName)
                {
                    var connection = await ApiEndpoints.FindConnectionAsync(connectionName, connections, cancellationToken);
                    if (connection is null)
                        return Results.BadRequest(new ApiError($"Unknown connection '{connectionName}'."));
                    partner.ConnectionId = connection.Id;
                    partner.Connection = connection;
                }
                else if (created)
                {
                    return Results.BadRequest(new ApiError("A new partner needs its connection."));
                }

                if (request.DefaultIdentity is { } defaultIdentity)
                {
                    if (defaultIdentity.Length == 0)
                    {
                        partner.DefaultIdentityId = null;
                        partner.DefaultIdentity = null;
                    }
                    else
                    {
                        var identity = (await identities.GetAllAsync(cancellationToken)).FirstOrDefault(i => ApiEndpoints.Matches(i.As2Id, defaultIdentity));
                        if (identity is null)
                            return Results.BadRequest(new ApiError($"Unknown identity '{defaultIdentity}'."));
                        partner.DefaultIdentityId = identity.Id;
                        partner.DefaultIdentity = identity;
                    }
                }

                partner.Name = request.Name?.Trim() ?? partner.Name;
                partner.Description = request.Description ?? partner.Description;
                partner.Enabled = request.Enabled ?? partner.Enabled;
                partner.ContentType = request.ContentType?.Trim() ?? partner.ContentType;
                partner.Subject = request.Subject ?? partner.Subject;

                try
                {
                    await dataService.SavePartnerAsync(partner);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ApiError(ex.Message));
                }

                return created
                    ? Results.Created($"/api/v1/partners/{Uri.EscapeDataString(partner.As2Id)}", PartnerDto.From(partner))
                    : Results.Ok(PartnerDto.From(partner));
            })
            .WithSummary("Creates the partner with the AS2 name or updates it.")
            .WithDescription("connection is the name of the connection (required for a new partner); defaultIdentity the AS2 name of an " +
                             "identity, an empty string removes it.");

        configuration.MapPut("/identities/{as2Id}", async (string as2Id, IdentityRequest request, IIdentityRepository identities,
                IDataService dataService, CancellationToken cancellationToken) =>
            {
                var identity = (await identities.GetAllAsync(cancellationToken)).FirstOrDefault(i => ApiEndpoints.Matches(i.As2Id, as2Id));
                var created = identity is null;
                identity ??= new Identity { As2Id = as2Id.Trim(), Name = as2Id.Trim() };
                var originalDecryptionCertificateId = identity.DecryptionCertificateId;

                identity.Name = request.Name?.Trim() ?? identity.Name;
                identity.Description = request.Description ?? identity.Description;
                identity.Email = request.Email ?? identity.Email;
                identity.SigningCertificateId = request.SigningCertificateId ?? identity.SigningCertificateId;
                identity.DecryptionCertificateId = request.DecryptionCertificateId ?? identity.DecryptionCertificateId;

                try
                {
                    await dataService.SaveIdentityAsync(identity, created ? null : originalDecryptionCertificateId);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ApiError(ex.Message));
                }

                var dto = new IdentityDto(identity.Id, identity.Name, identity.As2Id);
                return created ? Results.Created($"/api/v1/identities/{Uri.EscapeDataString(identity.As2Id)}", dto) : Results.Ok(dto);
            })
            .WithSummary("Creates our identity with the AS2 name or updates it.")
            .WithDescription("Certificates (with the private key) are given by the id from POST /certificates.");
    }
}

public record CertificateRequest(string FileName, string Data, string? Password, string? Name);

public record CertificateDto(int Id, string Name, string? Thumbprint, DateTime ValidFrom, DateTime ValidTo, bool HasPrivateKey)
{
    public static CertificateDto From(Certificate c) => new(c.Id, c.Name, c.Thumbprint, c.ValidFrom, c.ValidTo, c.HasPrivateKey);
}

/// <summary>The settings of a connection; a property left out (null) keeps its value, or the default of a new one.</summary>
public class ConnectionRequest
{
    public string? Url { get; init; }
    public string? Description { get; init; }
    public bool? SignMessages { get; init; }
    public string? SignatureAlgorithm { get; init; }
    public bool? EncryptMessages { get; init; }
    public string? EncryptionAlgorithm { get; init; }
    public bool? CompressMessages { get; init; }
    public bool? CompressBeforeSigning { get; init; }
    public bool? UnsignedWithoutMime { get; init; }
    public string? MdnMode { get; init; }
    public bool? RequestSignedMdn { get; init; }
    public int? MdnTimeoutMinutes { get; init; }
    public bool? RequireSignedMessages { get; init; }
    public bool? RequireEncryptedMessages { get; init; }
    public int? SignatureCertificateId { get; init; }
    public int? EncryptionCertificateId { get; init; }
    public int? TlsCertificateId { get; init; }
    public string? HttpUserName { get; init; }
    public string? HttpPassword { get; init; }
    public int? TimeoutSeconds { get; init; }
    /// <summary>Replaces all contacts when given.</summary>
    public List<PartnerContact>? Contacts { get; init; }

    public void ApplyTo(Connection c)
    {
        c.Url = Url?.Trim() ?? c.Url;
        c.Description = Description ?? c.Description;
        c.SignMessages = SignMessages ?? c.SignMessages;
        c.SignatureAlgorithm = SignatureAlgorithm?.Trim().ToLowerInvariant() ?? c.SignatureAlgorithm;
        c.EncryptMessages = EncryptMessages ?? c.EncryptMessages;
        c.EncryptionAlgorithm = EncryptionAlgorithm?.Trim().ToLowerInvariant() ?? c.EncryptionAlgorithm;
        c.CompressMessages = CompressMessages ?? c.CompressMessages;
        c.CompressBeforeSigning = CompressBeforeSigning ?? c.CompressBeforeSigning;
        c.UnsignedWithoutMime = UnsignedWithoutMime ?? c.UnsignedWithoutMime;
        c.MdnMode = MdnMode is null ? c.MdnMode : Enum.Parse<MdnMode>(MdnMode, ignoreCase: true);
        c.RequestSignedMdn = RequestSignedMdn ?? c.RequestSignedMdn;
        c.MdnTimeoutMinutes = MdnTimeoutMinutes ?? c.MdnTimeoutMinutes;
        c.RequireSignedMessages = RequireSignedMessages ?? c.RequireSignedMessages;
        c.RequireEncryptedMessages = RequireEncryptedMessages ?? c.RequireEncryptedMessages;
        c.SignatureCertificateId = SignatureCertificateId ?? c.SignatureCertificateId;
        c.EncryptionCertificateId = EncryptionCertificateId ?? c.EncryptionCertificateId;
        c.TlsCertificateId = TlsCertificateId ?? c.TlsCertificateId;
        c.HttpUserName = HttpUserName ?? c.HttpUserName;
        c.HttpPassword = HttpPassword ?? c.HttpPassword;
        c.TimeoutSeconds = TimeoutSeconds ?? c.TimeoutSeconds;
        c.Contacts = Contacts ?? c.Contacts;
    }
}

public record PartnerRequest(string? Name, string? Description, string? Connection, bool? Enabled, string? DefaultIdentity,
    string? ContentType, string? Subject);

public record IdentityRequest(string? Name, string? Description, string? Email, int? SigningCertificateId, int? DecryptionCertificateId);

public record ConnectionDto(int Id, string Name, string? Description, string Url, bool SignMessages, string SignatureAlgorithm,
    bool EncryptMessages, string EncryptionAlgorithm, bool CompressMessages, bool CompressBeforeSigning, bool UnsignedWithoutMime, string MdnMode,
    bool RequestSignedMdn, int MdnTimeoutMinutes, bool RequireSignedMessages, bool RequireEncryptedMessages,
    int? SignatureCertificateId, int? PreviousSignatureCertificateId, int? EncryptionCertificateId, int? TlsCertificateId,
    string? HttpUserName, int TimeoutSeconds, IReadOnlyList<PartnerContact> Contacts, IReadOnlyList<string> PartnerAs2Ids)
{
    public static ConnectionDto From(Connection c) => new(c.Id, c.Name, c.Description, c.Url, c.SignMessages, c.SignatureAlgorithm,
        c.EncryptMessages, c.EncryptionAlgorithm, c.CompressMessages, c.CompressBeforeSigning, c.UnsignedWithoutMime, c.MdnMode.ToString(), c.RequestSignedMdn,
        c.MdnTimeoutMinutes, c.RequireSignedMessages, c.RequireEncryptedMessages, c.SignatureCertificateId,
        c.PreviousSignatureCertificateId, c.EncryptionCertificateId, c.TlsCertificateId, c.HttpUserName, c.TimeoutSeconds,
        c.Contacts, c.Partners.Select(p => p.As2Id).Order(StringComparer.Ordinal).ToList());
}
