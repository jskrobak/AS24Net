using Microsoft.Extensions.Diagnostics.HealthChecks;
using AS24Net.Domain;
using AS24Net.Services.Health;

namespace AS24Net.Services.Tests;

public class HealthCheckTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0);

    [Fact]
    public void Certificates_ExpiringSoon_AreDegraded()
    {
        var certificate = new Certificate { Id = 1, Name = "CN=partner", ValidTo = Now.AddDays(10) };

        var result = CertificatesHealthCheck.Evaluate([new CertificatesHealthCheck.CertificateUse(certificate, "signature certificate of partner P")], Now);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("expires", result.Description);
    }

    [Fact]
    public void Certificates_ReplacedBeforeTheyExpire_AreHealthy()
    {
        var certificate = new Certificate { Id = 1, Name = "CN=partner", ValidTo = Now.AddDays(10) };

        var result = CertificatesHealthCheck.Evaluate(
            [new CertificatesHealthCheck.CertificateUse(certificate, "signature certificate of partner P", Now.AddDays(5))], Now);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void Certificates_ReplacedOnlyForOneUse_AreStillReported()
    {
        var certificate = new Certificate { Id = 1, Name = "CN=partner", ValidTo = Now.AddDays(10) };

        var result = CertificatesHealthCheck.Evaluate([
            new CertificatesHealthCheck.CertificateUse(certificate, "signature", Now.AddDays(5)),
            new CertificatesHealthCheck.CertificateUse(certificate, "encryption"),
        ], Now);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public void Messages_StuckOrFailed_AreDegraded()
    {
        Assert.Equal(HealthStatus.Healthy, MessagesHealthCheck.Evaluate(0, 0, 0, 0).Status);
        Assert.Equal(HealthStatus.Degraded, MessagesHealthCheck.Evaluate(1, 0, 0, 0).Status);
        Assert.Equal(HealthStatus.Degraded, MessagesHealthCheck.Evaluate(0, 0, 2, 0).Status);
        Assert.Contains("refused", MessagesHealthCheck.Evaluate(0, 0, 0, 3).Description);
    }

    [Fact]
    public void SendService_PausedIsDegraded_StuckIsUnhealthy()
    {
        Assert.Equal(HealthStatus.Healthy, SendServiceHealthCheck.Evaluate(true, false, Now, Now.AddSeconds(-10), 0, 30, Now).Status);
        Assert.Equal(HealthStatus.Degraded, SendServiceHealthCheck.Evaluate(true, true, Now, Now, 0, 30, Now).Status);
        Assert.Equal(HealthStatus.Unhealthy, SendServiceHealthCheck.Evaluate(true, false, Now, Now.AddMinutes(-10), 0, 30, Now).Status);
        Assert.Equal(HealthStatus.Unhealthy, SendServiceHealthCheck.Evaluate(false, false, null, null, 0, 30, Now).Status);
    }

    [Fact]
    public void HealthMonitor_ReportsOnlyChanges_AndAQuietStart()
    {
        var healthy = Report(("database", HealthStatus.Healthy), ("messages", HealthStatus.Healthy));
        var degraded = Report(("database", HealthStatus.Healthy), ("messages", HealthStatus.Degraded));

        Assert.Empty(HealthMonitor.Changes(null, healthy));
        var change = Assert.Single(HealthMonitor.Changes(healthy, degraded));
        Assert.Equal("messages", change.Name);
        Assert.Equal(HealthStatus.Healthy, change.Previous);
    }

    private static HealthReport Report(params (string Name, HealthStatus Status)[] entries) =>
        new(entries.ToDictionary(e => e.Name, e => new HealthReportEntry(e.Status, e.Name, TimeSpan.Zero, null, null)), TimeSpan.Zero);
}
