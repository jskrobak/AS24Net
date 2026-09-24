using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AS24Net.Domain;
using AS24Net.Services.As2;
using AS24Net.Services.ConnectionTests;

namespace AS24Net.Services.Tests;

public class ConnectionTestTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0);

    private static Certificate Valid(string name, bool privateKey = false, int days = 365) =>
        new() { Id = name.GetHashCode(), Name = name, ValidFrom = Now.AddYears(-1), ValidTo = Now.AddDays(days), HasPrivateKey = privateKey };

    private static (Partner Partner, Identity Identity) Configured() =>
    (
        new Partner
        {
            Name = "Partner", As2Id = "PARTNER",
            Connection = new Connection
            {
                Name = "Partner", Url = "https://as2.example.com/as2",
                SignMessages = true, EncryptMessages = true, MdnMode = MdnMode.Sync, RequestSignedMdn = true,
                SignatureCertificate = Valid("partner"), EncryptionCertificate = Valid("partner"),
            },
        },
        new Identity { Name = "Us", As2Id = "US", SigningCertificate = Valid("us", privateKey: true) }
    );

    [Fact]
    public void CompleteConfiguration_HasNoProblems()
    {
        var (partner, identity) = Configured();

        var (problems, warnings) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), Now);

        Assert.Empty(problems);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MissingCertificates_AreProblems()
    {
        var (partner, identity) = Configured();
        partner.Connection.EncryptionCertificate = null;
        partner.Connection.SignatureCertificate = null;
        identity.SigningCertificate = Valid("no key");

        var (problems, _) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), Now);

        Assert.Contains(problems, p => p.Contains("signing certificate") && p.Contains("no private key"));
        Assert.Contains(problems, p => p.Contains("encryption certificate of the partner is not set"));
        Assert.Contains(problems, p => p.Contains("signature certificate of the partner is not set"));
    }

    [Fact]
    public void CertificatesThatAreNotUsed_AreNotRequired()
    {
        var (partner, identity) = Configured();
        partner.Connection.SignMessages = partner.Connection.EncryptMessages = partner.Connection.RequireSignedMessages = false;
        partner.Connection.MdnMode = MdnMode.None;
        partner.Connection.SignatureCertificate = partner.Connection.EncryptionCertificate = null;
        identity.SigningCertificate = null;

        var (problems, _) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), Now);

        Assert.Empty(problems);
    }

    [Fact]
    public void ExpiredCertificate_IsAProblem_AndOneThatExpiresSoon_AWarning()
    {
        var (partner, identity) = Configured();
        partner.Connection.EncryptionCertificate = Valid("expired", days: -1);
        identity.SigningCertificate = Valid("soon", privateKey: true, days: 10);

        var (problems, warnings) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), Now);

        Assert.Contains(problems, p => p.Contains("expired"));
        Assert.Contains(warnings, w => w.Contains("soon") && w.Contains("expires"));
    }

    [Fact]
    public void AsynchronousMdn_NeedsThePublicUrl()
    {
        var (partner, identity) = Configured();
        partner.Connection.MdnMode = MdnMode.Async;

        var (problems, _) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings { PublicUrl = null }, Now);
        Assert.Contains(problems, p => p.Contains("public URL"));

        // A loopback address cannot be reached by a partner elsewhere.
        var (_, warnings) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings { PublicUrl = "http://localhost:5010/as2" }, Now);
        Assert.Contains(warnings, w => w.Contains("cannot reach our public URL"));
    }

    [Fact]
    public void PlainHttpWithoutEncryption_IsAWarning()
    {
        var (partner, identity) = Configured();
        partner.Connection.Url = "http://as2.example.com/as2";
        partner.Connection.EncryptMessages = false;

        var (_, warnings) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), Now);

        Assert.Contains(warnings, w => w.Contains("plain HTTP"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.MethodNotAllowed, true)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    public void HttpAnswers_ShowWhetherTheEndpointCanBeUsed(HttpStatusCode status, bool success)
    {
        Assert.Equal(success, ConnectionTestService.EvaluateResponse(status, null, new Connection()).Success);
    }

    [Fact]
    public void Unauthorized_SaysWhetherCredentialsAreSet()
    {
        Assert.Contains("set the user name", ConnectionTestService.EvaluateResponse(HttpStatusCode.Unauthorized, null, new Connection()).Message);
        Assert.Contains("refuses our HTTP user name",
            ConnectionTestService.EvaluateResponse(HttpStatusCode.Unauthorized, null, new Connection { HttpUserName = "us" }).Message);
    }

    [Theory]
    [InlineData(405, ConnectionTestStage.Completed, true)]
    [InlineData(401, ConnectionTestStage.Http, false)]
    public async Task Run_StopsAtTheAnswerOfTheEndpoint(int status, ConnectionTestStage stage, bool success)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        string? method = null;
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            method = context.Request.HttpMethod;
            context.Response.StatusCode = status;
            context.Response.Close();
        });

        var result = await RunAsync($"http://localhost:{port}/as2");
        await serve;

        Assert.Equal("GET", method);
        Assert.Equal(stage, result.Stage);
        Assert.Equal(success, result.Success);
        Assert.Equal(status, result.HttpStatus);
    }

    [Fact]
    public async Task Run_ReportsAClosedPort()
    {
        var result = await RunAsync($"http://127.0.0.1:{FreePort()}/as2");

        Assert.Equal(ConnectionTestStage.Connect, result.Stage);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Run_RefusesAnUnusableUrl()
    {
        var result = await RunAsync("ftp://as2.example.com/");

        Assert.Equal(ConnectionTestStage.Setup, result.Stage);
    }

    private static async Task<ConnectionTestResult> RunAsync(string url)
    {
        // Nothing to secure, so that only the connection decides.
        var partner = new Partner
        {
            Id = 1, Name = "Local", As2Id = "LOCAL",
            Connection = new Connection
            {
                Name = "Local", Url = url, TimeoutSeconds = 10,
                SignMessages = false, EncryptMessages = false, RequireSignedMessages = false, MdnMode = MdnMode.None,
            },
        };
        using var clients = new As2HttpClientProvider();
        var service = new ConnectionTestService(null!, null!, clients, null!, new ApplicationTimeService(new ConfigurationBuilder().Build()),
            NullLogger<ConnectionTestService>.Instance);
        return await service.RunAsync(partner, new Identity { Name = "Us", As2Id = "US" }, new GlobalSettings());
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public class ConnectionTestCertificateTests
{
    [Fact]
    public void ExpiredServerCertificate_IsCalledExpired()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=old.example.com", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var text = ConnectionTestService.DescribeCertificateErrors(System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors,
            pinned: false, certificate, new DateTime(2026, 9, 24));

        Assert.StartsWith("it expired on", text);
    }
}
