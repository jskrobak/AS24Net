using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Havit.Services.TimeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.As2;
using AS24Net.Services.Certificates;
using AS24Net.Services.TransferEvents;

namespace AS24Net.Services.ConnectionTests;

/// <summary>How far a connection test got; for a failed one, where it stopped.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConnectionTestStage>))]
public enum ConnectionTestStage
{
    /// <summary>The URL of the partner cannot be used at all.</summary>
    Setup,

    /// <summary>Resolving the host name and opening the TCP connection.</summary>
    Connect,

    /// <summary>The TLS handshake, with the partner's server certificate checked as when sending.</summary>
    Tls,

    /// <summary>The HTTP request to the partner's URL, with its basic authentication.</summary>
    Http,

    /// <summary>The endpoint answered; nothing was sent.</summary>
    Completed,
}

/// <param name="Problems">What keeps messages from being exchanged although the endpoint answers, e.g. a missing certificate.</param>
/// <param name="Warnings">What will need attention, e.g. a certificate that expires soon.</param>
public sealed record ConnectionTestResult(
    int PartnerId,
    string Partner,
    string Identity,
    string Url,
    DateTime Tested,
    TimeSpan Duration,
    ConnectionTestStage Stage,
    string Message,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings,
    int? HttpStatus = null,
    string? Tls = null,
    string? RemoteCertificate = null,
    string? Details = null)
{
    public bool Success => Stage == ConnectionTestStage.Completed && Problems.Count == 0;
}

/// <summary>
/// Tests the connection to partners without sending anything: the configuration of the partner and the identity
/// (certificates, public URL), the TCP connection, the TLS handshake with the partner's server certificate checked
/// as when sending, and an HTTP <c>GET</c> of the partner's URL with its basic authentication. AS2 has no request
/// that does nothing, so the test stops at the answer to that request; a message or an MDN is never sent. The send
/// queue is not touched and every test is written to the transfer log.
/// </summary>
public sealed class ConnectionTestService(
    IServiceScopeFactory serviceScopeFactory,
    GlobalSettingsService settingsService,
    As2HttpClientProvider httpClients,
    ITransferEventLog transferEvents,
    ITimeService timeService,
    ILogger<ConnectionTestService> logger)
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A certificate that expires within this time is reported as a warning.</summary>
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<int, ConnectionTestResult> _results = new();
    private readonly Lock _lock = new();
    private CancellationTokenSource? _run;

    /// <summary>The last result of every partner tested since the start of the application.</summary>
    public IReadOnlyDictionary<int, ConnectionTestResult> Results => _results;

    /// <summary>A series of tests is running.</summary>
    public bool IsRunning => _run is not null;

    /// <summary>Tests done and planned in the running series.</summary>
    public (int Done, int Total) Progress { get; private set; }

    /// <summary>The partner being tested at the moment.</summary>
    public int? Current { get; private set; }

    /// <summary>Raised when a test starts or ends.</summary>
    public event Action? Changed;

    /// <summary>
    /// Tests the partners one after another in the background. <paramref name="identityId"/> is the identity to test
    /// as; without it, the default identity of every partner. Returns false when a series is running already.
    /// </summary>
    public bool Start(IReadOnlyList<int> partnerIds, int? identityId)
    {
        CancellationTokenSource run;
        lock (_lock)
        {
            if (_run is not null)
                return false;
            _run = run = new CancellationTokenSource();
            Progress = (0, partnerIds.Count);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var partnerId in partnerIds)
                {
                    if (run.IsCancellationRequested)
                        break;

                    await TestAsync(partnerId, identityId, run.Token);
                    Progress = (Progress.Done + 1, Progress.Total);
                }
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection tests failed");
            }
            finally
            {
                lock (_lock)
                    _run = null;
                Current = null;
                run.Dispose();
                Changed?.Invoke();
            }
        });

        Changed?.Invoke();
        return true;
    }

    /// <summary>Stops the running series after the test in progress.</summary>
    public void Cancel()
    {
        lock (_lock)
            _run?.Cancel();
    }

    /// <param name="identityId">The identity to test as; without it the default identity of the partner, else the first one.</param>
    public async Task<ConnectionTestResult> TestAsync(int partnerId, int? identityId, CancellationToken cancellationToken = default)
    {
        Current = partnerId;
        Changed?.Invoke();
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var partner = await scope.ServiceProvider.GetRequiredService<IPartnerRepository>().FindWithRefsAsync(partnerId, cancellationToken)
                          ?? throw new InvalidOperationException($"Partner {partnerId} does not exist.");
            var identities = scope.ServiceProvider.GetRequiredService<IIdentityRepository>();
            var chosen = identityId ?? partner.DefaultIdentityId
                         ?? (await identities.GetAllAsync(cancellationToken)).OrderBy(i => i.Id).FirstOrDefault()?.Id
                         ?? throw new InvalidOperationException("No identity is configured.");
            var identity = await identities.FindWithRefsAsync(chosen, cancellationToken)
                           ?? throw new InvalidOperationException($"Identity {chosen} does not exist.");

            var result = await RunAsync(partner, identity, await settingsService.GetGlobalSettingsAsync(), cancellationToken);
            _results[partnerId] = result;
            Record(result);
            return result;
        }
        finally
        {
            Current = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Runs the test with partner and identity loaded with their certificates; nothing is stored.</summary>
    public async Task<ConnectionTestResult> RunAsync(Partner partner, Identity identity, GlobalSettings settings,
        CancellationToken cancellationToken = default)
    {
        var tested = timeService.GetCurrentTime();
        var stopwatch = Stopwatch.StartNew();
        var (problems, warnings) = CheckConfiguration(partner, identity, settings, tested);
        string? tls = null, remoteCertificate = null;
        int? httpStatus = null;

        ConnectionTestResult Result(ConnectionTestStage stage, string message, string? details = null) =>
            new(partner.Id, partner.Name, identity.As2Id, partner.Url, tested, stopwatch.Elapsed, stage, message, problems, warnings,
                httpStatus, tls, remoteCertificate, details);

        if (!Uri.TryCreate(partner.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Result(ConnectionTestStage.Setup, $"'{partner.Url}' is not an absolute http or https URL.");

        var stage = ConnectionTestStage.Connect;
        var timeout = TimeSpan.FromSeconds(Math.Min(partner.TimeoutSeconds, ConnectTimeout.TotalSeconds));
        try
        {
            using var client = new TcpClient();
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(timeout);
                try
                {
                    await client.ConnectAsync(uri.DnsSafeHost, uri.Port, connectTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"No connection to {uri.Authority} within {timeout.TotalSeconds:F0} seconds. A firewall may drop it.");
                }
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                stage = ConnectionTestStage.Tls;
                (tls, remoteCertificate) = await HandshakeAsync(client.GetStream(), uri.DnsSafeHost, partner, timeout, tested, warnings,
                    cancellationToken);
            }

            stage = ConnectionTestStage.Http;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var http = httpClients.CreateClient(partner);
            http.Timeout = timeout;
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The partner did not answer within {timeout.TotalSeconds:F0} seconds.");
            }

            using (response)
            {
                httpStatus = (int)response.StatusCode;
                var (success, message) = EvaluateResponse(response.StatusCode, response.ReasonPhrase, partner);
                if (!success)
                    return Result(ConnectionTestStage.Http, message);

                return Result(ConnectionTestStage.Completed, problems.Count == 0
                    ? message
                    : $"{message} The configuration has {problems.Count} problem(s), see below.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = Describe(ex, uri);
            logger.LogWarning("Connection test with {Partner} failed at {Stage}: {Message}", partner.Name, stage, message);
            return Result(stage, message, ex.ToString());
        }
    }

    /// <summary>
    /// The TLS handshake on its own, so that the protocol, the cipher suite and the certificate of the partner can be
    /// shown; the server certificate is checked as the HTTP client checks it when sending.
    /// </summary>
    private static async Task<(string Tls, string Certificate)> HandshakeAsync(NetworkStream stream, string host, Partner partner,
        TimeSpan timeout, DateTime now, List<string> warnings, CancellationToken cancellationToken)
    {
        using var anchor = partner.TlsCertificate is { } trusted ? CertificateLoader.Load(trusted) : null;
        X509Certificate2? remote = null;
        string? refusal = null;

        await using var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(timeout);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    remote = certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    var accepted = anchor is null
                        ? errors == SslPolicyErrors.None
                        : As2HttpClientProvider.IsTrusted(remote, errors, anchor);
                    if (!accepted)
                        refusal = DescribeCertificateErrors(errors, anchor is not null, remote, DateTime.Now);
                    return accepted;
                },
            }, handshakeTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The TLS handshake did not finish within {timeout.TotalSeconds:F0} seconds.");
        }
        catch (AuthenticationException ex) when (refusal is not null)
        {
            throw new AuthenticationException($"The server certificate of {partner.Name} is refused: {refusal}" +
                                              (remote is null ? "" : $" ({CertificateText(remote)})"), ex);
        }

        using (remote)
        {
            if (remote is not null && remote.NotAfter - now < ExpiryWarning)
                warnings.Add($"The HTTPS certificate of the partner expires on {remote.NotAfter:d}.");

            return ($"{ssl.SslProtocol}, {ssl.NegotiatedCipherSuite}", remote is null ? "" : CertificateText(remote));
        }
    }

    internal static string DescribeCertificateErrors(SslPolicyErrors errors, bool pinned, X509Certificate2? certificate, DateTime now)
    {
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) || certificate is null)
            return "the server presented no certificate.";
        if (certificate.NotAfter < now)
            return $"it expired on {certificate.NotAfter:d}.";
        if (certificate.NotBefore > now)
            return $"it is valid only from {certificate.NotBefore:d}.";
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            return "it is not issued for the host name of the URL.";
        return pinned
            ? "it is neither the certificate trusted for the partner's HTTPS nor issued by it."
            : "it is not trusted by the system trust store; select the partner's certificate or its CA for HTTPS.";
    }

    private static string CertificateText(X509Certificate2 certificate) =>
        $"{certificate.Subject}, issued by {certificate.Issuer}, valid {certificate.NotBefore:d} – {certificate.NotAfter:d}";

    /// <summary>
    /// Whether the answer to the <c>GET</c> shows an endpoint that messages can be posted to. Most AS2 servers answer a
    /// <c>GET</c> with an error of their own (e.g. 405, only <c>POST</c> is allowed), which shows as much as a success:
    /// the address exists and lets us in.
    /// </summary>
    public static (bool Success, string Message) EvaluateResponse(HttpStatusCode status, string? reason, Partner partner)
    {
        var code = $"HTTP {(int)status}{(string.IsNullOrWhiteSpace(reason) ? "" : " " + reason)}";
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.ProxyAuthenticationRequired => (false, string.IsNullOrEmpty(partner.HttpUserName)
                ? $"The partner wants HTTP authentication ({code}): set the user name and password of the partner."
                : $"The partner refuses our HTTP user name or password ({code})."),
            HttpStatusCode.Forbidden => (false, $"The partner refuses access ({code}); it may not accept our address."),
            HttpStatusCode.NotFound => (false, $"The URL does not exist at the partner ({code}); check the path."),
            HttpStatusCode.MethodNotAllowed => (true, $"The endpoint answered {code}: it accepts only POST, as an AS2 endpoint does. Nothing was sent."),
            >= HttpStatusCode.InternalServerError => (false, $"The partner's server has a problem ({code})."),
            >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => (true, $"The endpoint answered {code} (a redirect is not followed when sending). Nothing was sent."),
            _ => (true, $"The endpoint answered {code}. Nothing was sent."),
        };
    }

    /// <summary>
    /// What in the configuration of the partner and the identity keeps messages from being sent or received
    /// (problems), and what will need attention soon (warnings). The same conditions make a message fail when it is
    /// sent or refused when it comes.
    /// </summary>
    public static (List<string> Problems, List<string> Warnings) CheckConfiguration(Partner partner, Identity identity,
        GlobalSettings settings, DateTime now)
    {
        var problems = new List<string>();
        var warnings = new List<string>();

        void Check(Certificate? certificate, string name, bool required, bool privateKey = false)
        {
            if (certificate is null)
            {
                if (required)
                    problems.Add($"{name} is not set.");
                return;
            }

            if (privateKey && !certificate.HasPrivateKey)
                problems.Add($"{name} {certificate.Name} has no private key.");
            else if (certificate.ValidTo < now)
                problems.Add($"{name} {certificate.Name} expired on {certificate.ValidTo:d}.");
            else if (certificate.ValidFrom > now)
                problems.Add($"{name} {certificate.Name} is valid only from {certificate.ValidFrom:d}.");
            else if (certificate.ValidTo - now < ExpiryWarning)
                warnings.Add($"{name} {certificate.Name} expires on {certificate.ValidTo:d}.");
        }

        if (!partner.Enabled)
            warnings.Add("The partner is disabled: nothing is sent to it and its messages are refused.");

        if (partner.SignMessages)
            Check(identity.SigningCertificate, $"The signing certificate of identity {identity.Name}", required: true, privateKey: true);
        if (partner.EncryptMessages)
            Check(partner.EncryptionCertificate, "The encryption certificate of the partner", required: true);

        var verifies = partner.RequireSignedMessages || partner is { MdnMode: not MdnMode.None, RequestSignedMdn: true };
        Check(partner.SignatureCertificate, "The signature certificate of the partner", required: verifies);

        var decryption = identity.DecryptionCertificate ?? identity.SigningCertificate;
        Check(decryption, $"The decryption certificate of identity {identity.Name}", required: partner.RequireEncryptedMessages, privateKey: true);

        if (partner.MdnMode == MdnMode.Async)
        {
            if (string.IsNullOrWhiteSpace(settings.PublicUrl))
                problems.Add("An asynchronous MDN needs the public URL of this server (Settings).");
            else if (!Uri.TryCreate(settings.PublicUrl, UriKind.Absolute, out var publicUrl) || publicUrl.IsLoopback && !IsLoopback(partner.Url))
                warnings.Add($"The partner cannot reach our public URL {settings.PublicUrl} to post the asynchronous MDN.");
        }

        if (Uri.TryCreate(partner.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp && !partner.EncryptMessages)
            warnings.Add("Messages go over plain HTTP without encryption: anybody on the way can read them.");

        return (problems, warnings);
    }

    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;

    /// <summary>What went wrong, in the words an administrator can act on.</summary>
    public static string Describe(Exception exception, Uri uri) => exception switch
    {
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } =>
            $"The host name {uri.DnsSafeHost} cannot be resolved.",
        SocketException socket => $"No connection to {uri.Authority}: {socket.Message}",
        AuthenticationException tls when tls.InnerException is not null && tls.Message.StartsWith("The server certificate") => tls.Message,
        AuthenticationException tls => $"TLS failed: {Innermost(tls)} The partner may require a TLS version or client certificate we do not offer.",
        TimeoutException timeout => timeout.Message,
        HttpRequestException http => $"The HTTP request failed: {Innermost(http)}",
        IOException io => $"The partner closed the connection: {Innermost(io)}",
        _ => exception.Message,
    };

    private static string Innermost(Exception exception)
    {
        var inner = exception;
        while (inner.InnerException is not null)
            inner = inner.InnerException;
        var text = inner.Message.Trim();
        return text.EndsWith('.') ? text : text + ".";
    }

    private void Record(ConnectionTestResult result)
    {
        var details = string.Join("\n", new[]
        {
            result.HttpStatus is null ? null : $"HTTP status: {result.HttpStatus}",
            result.Tls is null ? null : $"TLS: {result.Tls}",
            string.IsNullOrEmpty(result.RemoteCertificate) ? null : $"Certificate: {result.RemoteCertificate}",
        }.Concat(result.Problems.Select(p => $"Problem: {p}"))
         .Concat(result.Warnings.Select(w => $"Warning: {w}"))
         .Append(result.Details)
         .Where(l => l is not null));

        transferEvents.Record(new TransferEvent
        {
            Timestamp = result.Tested,
            Category = TransferEventCategory.Outgoing,
            Type = TransferEventType.ConnectionTested,
            Level = result.Success ? result.Warnings.Count == 0 ? TransferEventLevel.Information : TransferEventLevel.Warning : TransferEventLevel.Error,
            PartnerId = result.PartnerId,
            PartnerName = result.Partner,
            RemoteEndPoint = result.Url,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            Message = result.Stage == ConnectionTestStage.Completed
                ? $"Connection test with {result.Partner} as {result.Identity}: {result.Message}"
                : $"Connection test with {result.Partner} as {result.Identity} stopped at {result.Stage}: {result.Message}",
            Details = details.Length == 0 ? null : details,
        });
    }
}
