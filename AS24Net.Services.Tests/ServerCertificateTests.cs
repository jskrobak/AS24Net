using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AS24Net.Services.ConnectionTests;

namespace AS24Net.Services.Tests;

public class ServerCertificateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fetch_ReadsTheCertificateOfTheServer_ValidOrExpired(bool expired)
    {
        using var certificate = CreateCertificate(expired);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeOnceAsync(listener, certificate);

        try
        {
            var fetched = await ServerCertificateService.FetchAsync($"https://localhost:{port}/as2", TimeSpan.FromSeconds(10));

            Assert.Equal("localhost", fetched.Host);
            Assert.Equal(certificate.RawData, fetched.RawData);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(certificate.RawData)), fetched.Sha256Fingerprint);
            Assert.Equal(certificate.NotAfter, fetched.ValidTo);
        }
        finally
        {
            listener.Stop();
            await Task.WhenAny(server, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task Fetch_RefusesAPlainHttpUrl()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServerCertificateService.FetchAsync("http://localhost/as2", TimeSpan.FromSeconds(1)));

        Assert.Contains("not an https URL", ex.Message);
    }

    private static X509Certificate2 CreateCertificate(bool expired)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var from = expired ? DateTimeOffset.UtcNow.AddYears(-2) : DateTimeOffset.UtcNow.AddDays(-1);
        using var created = request.CreateSelfSigned(from, from.AddYears(1));
        // SslStream needs the key in a form the platform can use for the handshake.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    private static async Task ServeOnceAsync(TcpListener listener, X509Certificate2 certificate)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var ssl = new SslStream(client.GetStream());
            await ssl.AuthenticateAsServerAsync(certificate);
            // The client closes the connection right after the handshake.
            await ssl.ReadAsync(new byte[1]);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
