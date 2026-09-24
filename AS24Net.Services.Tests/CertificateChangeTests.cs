using AS24Net.Domain;
using AS24Net.Services.Certificates;
using AS24Net.Services.Health;

namespace AS24Net.Services.Tests;

public class CertificateChangeTests
{
    private static Connection Connection() => new() { SignatureCertificateId = 1, EncryptionCertificateId = 1, TlsCertificateId = 5 };

    [Fact]
    public void Signature_KeepsTheReplacedCertificateAsThePreviousOne()
    {
        var connection = Connection();

        CertificateChangeService.Apply(connection, 2, PartnerCertificateUsage.Signature);

        Assert.Equal(2, connection.SignatureCertificateId);
        Assert.Equal(1, connection.PreviousSignatureCertificateId);
        Assert.Equal(1, connection.EncryptionCertificateId);
    }

    [Fact]
    public void SignatureAndEncryption_ReplacesBoth()
    {
        var connection = Connection();

        CertificateChangeService.Apply(connection, 2, PartnerCertificateUsage.SignatureAndEncryption);

        Assert.Equal(2, connection.SignatureCertificateId);
        Assert.Equal(2, connection.EncryptionCertificateId);
        Assert.Equal(1, connection.PreviousSignatureCertificateId);
        Assert.Equal(5, connection.TlsCertificateId);
    }

    [Fact]
    public void Encryption_LeavesTheSignatureAlone()
    {
        var connection = Connection();

        CertificateChangeService.Apply(connection, 2, PartnerCertificateUsage.Encryption);

        Assert.Equal(1, connection.SignatureCertificateId);
        Assert.Null(connection.PreviousSignatureCertificateId);
        Assert.Equal(2, connection.EncryptionCertificateId);
    }

    [Fact]
    public void TheSameCertificateAgain_DoesNotLoseThePreviousOne()
    {
        var connection = Connection();
        CertificateChangeService.Apply(connection, 2, PartnerCertificateUsage.Signature);

        CertificateChangeService.Apply(connection, 2, PartnerCertificateUsage.Signature);

        Assert.Equal(1, connection.PreviousSignatureCertificateId);
    }

    [Fact]
    public void Tls_ReplacesTheTrustedServerCertificate()
    {
        var connection = Connection();

        CertificateChangeService.Apply(connection, 7, PartnerCertificateUsage.Tls);

        Assert.Equal(7, connection.TlsCertificateId);
        Assert.Equal(1, connection.SignatureCertificateId);
    }

    [Fact]
    public void HealthCheck_ReportsAnOverdueChangeAndACertificateExpiringBeforeItsTime()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var overdue = new CertificateChange { ConnectionName = "A", ActivateAt = now.AddHours(-1), Certificate = new Certificate { ValidTo = now.AddYears(1) } };
        var expiring = new CertificateChange { ConnectionName = "B", ActivateAt = now.AddDays(10), Certificate = new Certificate { ValidTo = now.AddDays(5) } };

        var result = CertificateChangesHealthCheck.Evaluate([overdue, expiring], [], now);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("connection A", result.Description);
        Assert.Contains("connection B", result.Description);
    }

    [Fact]
    public void HealthCheck_IsHealthyWithChangesOnTime()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var change = new CertificateChange { ConnectionName = "A", ActivateAt = now.AddDays(3), Certificate = new Certificate { ValidTo = now.AddYears(1) } };

        var result = CertificateChangesHealthCheck.Evaluate([change], [], now);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }
}
