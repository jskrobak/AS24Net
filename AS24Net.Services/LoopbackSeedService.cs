using Havit.Data.Patterns.UnitOfWorks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Certificates;

namespace AS24Net.Services;

public sealed class LoopbackSeedOptions
{
    public bool Enabled { get; set; }

    /// <summary>AS2 endpoint of this server, e.g. <c>http://localhost:5010/as2</c>.</summary>
    public string Url { get; set; } = "http://localhost:5010/as2";

    public string StationA { get; set; } = "AS24NET-A";
    public string StationB { get; set; } = "AS24NET-B";
}

/// <summary>
/// Development only (configuration <c>SeedLoopback</c>): two identities that are each other's partner on this
/// server, so a message sent from A to B goes through the whole way (signing, encryption, compression, HTTP, the
/// receipt and the MDN) without a second installation.
/// </summary>
public class LoopbackSeedService(
    IConfiguration configuration,
    IIdentityRepository identityRepository,
    IUnitOfWork unitOfWork,
    ILogger<LoopbackSeedService> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var options = configuration.GetSection("SeedLoopback").Get<LoopbackSeedOptions>();
        if (options is not { Enabled: true })
            return;

        var identities = await identityRepository.GetAllAsync(cancellationToken);
        if (identities.Any(i => i.As2Id == options.StationA || i.As2Id == options.StationB))
            return;

        var certificate = SelfSignedCertificateService.CreateEntity("as24net-loopback", TimeSpan.FromDays(3 * 365));
        certificate.Name = "Loopback: " + certificate.Name;
        unitOfWork.AddForInsert(certificate);

        var a = new Identity { Name = "Loopback A", As2Id = options.StationA, SigningCertificate = certificate, DecryptionCertificate = certificate };
        var b = new Identity { Name = "Loopback B", As2Id = options.StationB, SigningCertificate = certificate, DecryptionCertificate = certificate };
        unitOfWork.AddRangeForInsert([a, b]);

        // Each station is the partner of the other one: messages from A to B arrive here as messages of partner A.
        var sync = Connection("Loopback (synchronous MDN)", options.Url, certificate, MdnMode.Sync);
        var async = Connection("Loopback (asynchronous MDN)", options.Url, certificate, MdnMode.Async);
        unitOfWork.AddRangeForInsert([sync, async]);
        unitOfWork.AddRangeForInsert([
            Partner("Loopback A", options.StationA, sync, b),
            Partner("Loopback B", options.StationB, async, a),
        ]);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogWarning("Created the loopback stations {A} and {B} at {Url} for development", options.StationA, options.StationB, options.Url);
    }

    private static Connection Connection(string name, string url, Certificate certificate, MdnMode mdn) => new()
    {
        Name = name,
        Url = url,
        SignatureCertificate = certificate,
        EncryptionCertificate = certificate,
        CompressMessages = true,
        MdnMode = mdn,
        MdnTimeoutMinutes = 10,
    };

    private static Partner Partner(string name, string as2Id, Connection connection, Identity sender) => new()
    {
        Name = name,
        As2Id = as2Id,
        Connection = connection,
        DefaultIdentity = sender,
        ContentType = "application/edifact",
    };
}
