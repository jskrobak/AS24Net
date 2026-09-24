using AS24Net.Domain;
using AS24Net.Services.Certificates;
using AS24Net.Services.Health;

namespace AS24Net.Services.Tests;

public class CertificateChangeTests
{
    private static Partner Partner() => new() { SignatureCertificateId = 1, EncryptionCertificateId = 1, TlsCertificateId = 5 };

    [Fact]
    public void Signature_KeepsTheReplacedCertificateAsThePreviousOne()
    {
        var partner = Partner();

        CertificateChangeService.Apply(partner, 2, PartnerCertificateUsage.Signature);

        Assert.Equal(2, partner.SignatureCertificateId);
        Assert.Equal(1, partner.PreviousSignatureCertificateId);
        Assert.Equal(1, partner.EncryptionCertificateId);
    }

    [Fact]
    public void SignatureAndEncryption_ReplacesBoth()
    {
        var partner = Partner();

        CertificateChangeService.Apply(partner, 2, PartnerCertificateUsage.SignatureAndEncryption);

        Assert.Equal(2, partner.SignatureCertificateId);
        Assert.Equal(2, partner.EncryptionCertificateId);
        Assert.Equal(1, partner.PreviousSignatureCertificateId);
        Assert.Equal(5, partner.TlsCertificateId);
    }

    [Fact]
    public void Encryption_LeavesTheSignatureAlone()
    {
        var partner = Partner();

        CertificateChangeService.Apply(partner, 2, PartnerCertificateUsage.Encryption);

        Assert.Equal(1, partner.SignatureCertificateId);
        Assert.Null(partner.PreviousSignatureCertificateId);
        Assert.Equal(2, partner.EncryptionCertificateId);
    }

    [Fact]
    public void TheSameCertificateAgain_DoesNotLoseThePreviousOne()
    {
        var partner = Partner();
        CertificateChangeService.Apply(partner, 2, PartnerCertificateUsage.Signature);

        CertificateChangeService.Apply(partner, 2, PartnerCertificateUsage.Signature);

        Assert.Equal(1, partner.PreviousSignatureCertificateId);
    }

    [Fact]
    public void Tls_ReplacesTheTrustedServerCertificate()
    {
        var partner = Partner();

        CertificateChangeService.Apply(partner, 7, PartnerCertificateUsage.Tls);

        Assert.Equal(7, partner.TlsCertificateId);
        Assert.Equal(1, partner.SignatureCertificateId);
    }

    [Fact]
    public void HealthCheck_ReportsAnOverdueChangeAndACertificateExpiringBeforeItsTime()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var overdue = new CertificateChange { PartnerName = "A", ActivateAt = now.AddHours(-1), Certificate = new Certificate { ValidTo = now.AddYears(1) } };
        var expiring = new CertificateChange { PartnerName = "B", ActivateAt = now.AddDays(10), Certificate = new Certificate { ValidTo = now.AddDays(5) } };

        var result = CertificateChangesHealthCheck.Evaluate([overdue, expiring], [], now);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("partner A", result.Description);
        Assert.Contains("partner B", result.Description);
    }

    [Fact]
    public void HealthCheck_IsHealthyWithChangesOnTime()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var change = new CertificateChange { PartnerName = "A", ActivateAt = now.AddDays(3), Certificate = new Certificate { ValidTo = now.AddYears(1) } };

        var result = CertificateChangesHealthCheck.Evaluate([change], [], now);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }
}
