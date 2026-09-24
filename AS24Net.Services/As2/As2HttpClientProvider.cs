using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AS24Net.Core.As2;
using AS24Net.Domain;
using AS24Net.Services.Certificates;

namespace AS24Net.Services.As2;

/// <summary>
/// HTTP clients for partners. The connections are pooled per trust setting: the system trust store, or the
/// certificate trusted for the partner's HTTPS server (its own, pinned, or the CA that issued it).
/// </summary>
public sealed class As2HttpClientProvider : IDisposable
{
    private readonly ConcurrentDictionary<string, SocketsHttpHandler> _handlers = new();

    /// <summary>A client for one request to a partner of the connection; dispose it, the connections stay pooled.</summary>
    public HttpClient CreateClient(Connection connection)
    {
        var trusted = connection.TlsCertificate;
        var key = trusted?.Thumbprint ?? trusted?.Id.ToString() ?? "";
        var handler = _handlers.GetOrAdd(key, _ => CreateHandler(trusted));

        var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(connection.TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{As2Headers.Product}/1.0");
        if (!string.IsNullOrEmpty(connection.HttpUserName))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.HttpUserName}:{connection.HttpPassword}")));
        return client;
    }

    /// <summary>Forgets the pooled connections, e.g. after the trusted certificate of a connection changed.</summary>
    public void Reset()
    {
        foreach (var key in _handlers.Keys.ToList())
            if (_handlers.TryRemove(key, out var handler))
                handler.Dispose();
    }

    private static SocketsHttpHandler CreateHandler(Certificate? trusted)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = false,
        };

        if (trusted is not null)
        {
            var anchor = CertificateLoader.Load(trusted);
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    IsTrusted(certificate as X509Certificate2 ?? (certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData())),
                        errors, anchor),
            };
        }

        return handler;
    }

    /// <summary>
    /// The server certificate is the trusted one itself, or it was issued by it; the host name has to match in the
    /// second case.
    /// </summary>
    internal static bool IsTrusted(X509Certificate2? certificate, SslPolicyErrors errors, X509Certificate2 anchor)
    {
        if (certificate is null)
            return false;

        if (certificate.RawData.AsSpan().SequenceEqual(anchor.RawData))
            return true;

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(anchor);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!chain.Build(certificate))
            return false;

        // The trusted certificate may also be an intermediate CA, then the chain passes through it.
        return chain.ChainElements.Any(e => e.Certificate.RawData.AsSpan().SequenceEqual(anchor.RawData));
    }

    public void Dispose() => Reset();
}
