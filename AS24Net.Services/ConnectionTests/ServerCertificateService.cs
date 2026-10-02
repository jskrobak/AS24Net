using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Havit.Services.TimeServices;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Certificates;

namespace AS24Net.Services.ConnectionTests;

/// <summary>The certificate a partner's HTTPS server presents, as it was read; nothing about it is checked.</summary>
public sealed record ServerCertificate(
    string Host,
    byte[] RawData,
    string Subject,
    string Issuer,
    DateTime ValidFrom,
    DateTime ValidTo,
    string Sha256Fingerprint)
{
    public static ServerCertificate From(string host, X509Certificate2 certificate) => new(host, certificate.RawData,
        certificate.Subject, certificate.Issuer, certificate.NotBefore, certificate.NotAfter,
        Convert.ToHexString(SHA256.HashData(certificate.RawData)));
}

/// <summary>
/// Takes the certificate of a partner's HTTPS server as the certificate its signatures are verified with, for
/// partners whose AS2 software signs with the key of their HTTPS server. Most partners use a different certificate
/// for AS2, so it is done only on request, after the administrator compared the fingerprint.
/// </summary>
public sealed class ServerCertificateService(
    IPartnerRepository partnerRepository,
    CertificateChangeService changeService,
    ITimeService timeService,
    ShadowMode? shadowMode = null)
{
    /// <summary>The partner with its connection and the connection's current certificates.</summary>
    public async Task<Partner> GetPartnerAsync(int partnerId, CancellationToken cancellationToken = default) =>
        await partnerRepository.FindWithRefsAsync(partnerId, cancellationToken)
        ?? throw new InvalidOperationException($"Partner {partnerId} does not exist.");

    /// <summary>Reads the certificate the HTTPS server of the partner presents.</summary>
    public async Task<ServerCertificate> FetchAsync(int partnerId, CancellationToken cancellationToken = default)
    {
        // Partners are not to see the shadow server at all.
        if (shadowMode?.Enabled == true)
            throw new InvalidOperationException(ShadowMode.Reason);

        var connection = (await GetPartnerAsync(partnerId, cancellationToken)).Connection;
        return await FetchAsync(connection.Url,
            TimeSpan.FromSeconds(Math.Min(connection.TimeoutSeconds, ConnectionTestService.ConnectTimeout.TotalSeconds)), cancellationToken);
    }

    /// <summary>
    /// Reads the certificate the HTTPS server at the URL presents. Every certificate is accepted, expired or not
    /// trusted ones too, because it is only read and shown; nothing is sent after the handshake.
    /// </summary>
    public static async Task<ServerCertificate> FetchAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"'{url}' is not an https URL: the partner's server presents no certificate.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        X509Certificate2? remote = null;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.DnsSafeHost, uri.Port, timeoutSource.Token);
            await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = uri.DnsSafeHost,
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    remote = certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    return true;
                },
            }, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"No TLS handshake with {uri.Authority} within {timeout.TotalSeconds:F0} seconds.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or System.Security.Authentication.AuthenticationException)
        {
            throw new InvalidOperationException($"No TLS handshake with {uri.Authority}: {ex.Message}", ex);
        }

        using (remote)
        {
            return remote is null
                ? throw new InvalidOperationException($"The server {uri.Authority} presented no certificate.")
                : ServerCertificate.From(uri.DnsSafeHost, remote);
        }
    }

    /// <summary>
    /// Makes the certificate the one the signatures of the partner's connection, so of all its partners, are verified
    /// with, from now on: a certificate change applied right away, logged and reported like any other. The current
    /// signature certificate becomes the previous one, which is still accepted.
    /// </summary>
    public async Task<CertificateChange> UseAsSignatureCertificateAsync(int partnerId, ServerCertificate certificate, string? createdBy,
        CancellationToken cancellationToken = default)
    {
        var partner = await GetPartnerAsync(partnerId, cancellationToken);
        return await changeService.ScheduleFileAsync(partner.ConnectionId, certificate.RawData, $"{certificate.Host}.cer",
            PartnerCertificateUsage.Signature, timeService.GetCurrentTime(),
            $"Taken from the HTTPS server {certificate.Host} (SHA-256 {certificate.Sha256Fingerprint}) in the connection test of {partner.Name}",
            createdBy, cancellationToken);
    }
}
