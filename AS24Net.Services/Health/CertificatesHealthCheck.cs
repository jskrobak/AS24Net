using Havit.Services.TimeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;

namespace AS24Net.Services.Health;

/// <summary>
/// No certificate in use expired or expires within <see cref="WarningDays"/> days: ours (signing and decryption of
/// the identities) and those of the partners, unless a scheduled change replaces it before it expires.
/// Certificates stored but not used, and those kept only for a roll-over, are left out.
/// </summary>
public sealed class CertificatesHealthCheck(IServiceScopeFactory serviceScopeFactory, ITimeService timeService) : IHealthCheck
{
    public const int WarningDays = 30;

    public sealed record CertificateUse(Certificate Certificate, string Usage, DateTime? ReplacedAt = null);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var certificates = (await scope.ServiceProvider.GetRequiredService<ICertificateRepository>().GetAllAsync(cancellationToken))
            .ToDictionary(c => c.Id);
        var partners = await scope.ServiceProvider.GetRequiredService<IPartnerRepository>().GetAllAsync(cancellationToken);
        var identities = await scope.ServiceProvider.GetRequiredService<IIdentityRepository>().GetAllAsync(cancellationToken);
        var changes = await scope.ServiceProvider.GetRequiredService<ICertificateChangeRepository>().GetScheduledAsync(cancellationToken);

        var uses = new List<(int? Id, string Usage, DateTime? ReplacedAt)>();
        foreach (var identity in identities)
        {
            uses.Add((identity.SigningCertificateId, $"signing certificate of identity {identity.Name}", null));
            uses.Add((identity.DecryptionCertificateId, $"decryption certificate of identity {identity.Name}", null));
        }

        foreach (var partner in partners.Where(p => p.Enabled))
        {
            DateTime? Replaced(params PartnerCertificateUsage[] usages) => changes
                .Where(c => c.PartnerId == partner.Id && usages.Contains(c.Usage))
                .Min(c => (DateTime?)c.ActivateAt);

            uses.Add((partner.SignatureCertificateId, $"signature certificate of partner {partner.Name}",
                Replaced(PartnerCertificateUsage.Signature, PartnerCertificateUsage.SignatureAndEncryption)));
            uses.Add((partner.EncryptionCertificateId, $"encryption certificate of partner {partner.Name}",
                Replaced(PartnerCertificateUsage.Encryption, PartnerCertificateUsage.SignatureAndEncryption)));
            uses.Add((partner.TlsCertificateId, $"TLS certificate trusted for partner {partner.Name}", Replaced(PartnerCertificateUsage.Tls)));
        }

        return Evaluate(
            uses.Where(u => u.Id is { } id && certificates.ContainsKey(id))
                .Select(u => new CertificateUse(certificates[u.Id!.Value], u.Usage, u.ReplacedAt))
                .ToList(),
            timeService.GetCurrentTime());
    }

    public static HealthCheckResult Evaluate(IReadOnlyCollection<CertificateUse> uses, DateTime now)
    {
        var certificates = uses
            .GroupBy(u => u.Certificate.Id)
            .Select(g => (Certificate: g.First().Certificate,
                Usages: string.Join(", ", g.Select(u => u.Usage).Distinct()),
                // Replaced in time only when every use of it is.
                ReplacedAt: g.All(u => u.ReplacedAt is not null) ? g.Max(u => u.ReplacedAt) : null))
            .OrderBy(c => c.Certificate.ValidTo)
            .ToList();

        if (certificates.Count == 0)
            return HealthCheckResult.Healthy("No certificate is in use.");

        var data = certificates.ToDictionary(c => $"{c.Certificate.Name} (#{c.Certificate.Id})",
            c => (object)$"valid to {c.Certificate.ValidTo:s}; {c.Usages}");

        var problems = certificates
            .Where(c => c.Certificate.ValidTo < now.AddDays(WarningDays))
            .Where(c => c.ReplacedAt is null || c.ReplacedAt > c.Certificate.ValidTo)
            .Select(c => c.Certificate.ValidTo < now
                ? $"Certificate {c.Certificate.Name} ({c.Usages}) expired on {c.Certificate.ValidTo:d}."
                : $"Certificate {c.Certificate.Name} ({c.Usages}) expires on {c.Certificate.ValidTo:d}.")
            .ToList();

        if (problems.Count > 0)
            return HealthCheckResult.Degraded(string.Join(" ", problems), data: data);

        return HealthCheckResult.Healthy(
            $"{certificates.Count} certificate(s) in use, the first one expires on {certificates[0].Certificate.ValidTo:d}.", data);
    }
}
