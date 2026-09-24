using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AS24Net.Core.As2;
using AS24Net.Core.Security;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;

namespace AS24Net.Services.As2;

/// <summary>
/// Posts asynchronous MDNs to the URLs partners asked for (Receipt-Delivery-Option), with retries and back-off.
/// </summary>
public class AsyncMdnService(
    IServiceScopeFactory serviceScopeFactory,
    GlobalSettingsService settingsService,
    ITimeService timeService,
    As2HttpClientProvider httpClients,
    As2EventNotifier notifier,
    ILogger<AsyncMdnService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _trigger = new(0, 1);

    public DateTime? LastRun { get; private set; }

    public void Trigger()
    {
        try
        {
            _trigger.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already triggered.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendDueAsync(stoppingToken);
                LastRun = timeService.GetCurrentTime();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending asynchronous MDNs failed");
            }

            try
            {
                await _trigger.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task SendDueAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetGlobalSettingsAsync();
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReceivedMessageRepository>();
        var identities = scope.ServiceProvider.GetRequiredService<IIdentityRepository>();
        var partners = scope.ServiceProvider.GetRequiredService<IPartnerRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        foreach (var message in await repository.GetMdnsToSendAsync(cancellationToken))
        {
            var identity = message.IdentityId is { } identityId ? await identities.FindWithRefsAsync(identityId, cancellationToken) : null;
            var partner = message.PartnerId is { } partnerId ? await partners.FindWithRefsAsync(partnerId, cancellationToken) : null;
            string? error = null;
            try
            {
                if (partner is null)
                    throw new InvalidOperationException("The partner does not exist any more.");

                // Only a verified signature proves that the partner sent the message; otherwise anybody using its AS2
                // name could make this server post to any address.
                if (!message.Signed && !SameHost(message.MdnUrl, partner.Connection.Url))
                {
                    message.MdnRetryCount = settings.MaxMdnRetryCount;
                    throw new InvalidOperationException(
                        $"The message is not signed, so its MDN is posted only to the host of the partner's URL, not to {message.MdnUrl}.");
                }

                var mdn = BuildMdn(message, identity, message.Status == ReceivedStatus.Failed ? message.Error : null);
                await PostAsync(partner, message.MdnUrl!, mdn, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                error = ex is HttpRequestException or TaskCanceledException or InvalidOperationException ? ex.Message : ex.ToString();
            }

            var now = timeService.GetCurrentTime();
            var willRetry = false;
            if (error is null)
            {
                message.MdnStatus = MdnDeliveryStatus.SentAsync;
                message.MdnSentDate = now;
                message.MdnLastError = null;
            }
            else
            {
                message.MdnRetryCount++;
                message.MdnLastError = MdnEvaluation.Truncate(error, 2000);
                willRetry = message.MdnRetryCount <= settings.MaxMdnRetryCount && partner is not null;
                if (willRetry)
                {
                    message.MdnStatus = MdnDeliveryStatus.Retrying;
                    message.MdnNextRetry = now.Add(As2Sender.RetryDelay(message.MdnRetryCount, settings));
                }
                else
                {
                    message.MdnStatus = MdnDeliveryStatus.Failed;
                }

                logger.LogWarning("The asynchronous MDN for {MessageId} could not be posted to {Url}: {Error}", message.MessageId, message.MdnUrl, error);
            }

            unitOfWork.AddForUpdate(message);
            await unitOfWork.CommitAsync(cancellationToken);
            notifier.MdnSent(message, async: true, error, willRetry);
        }
    }

    internal static bool SameHost(string? url, string partnerUrl) =>
        Uri.TryCreate(url, UriKind.Absolute, out var a) && Uri.TryCreate(partnerUrl, UriKind.Absolute, out var b)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private async Task PostAsync(Partner partner, string url, As2OutboundMdn mdn, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"'{url}' is not an http or https URL.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(mdn.Body) };
        foreach (var (name, value) in mdn.Headers)
        {
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                request.Content.Headers.TryAddWithoutValidation(name, value);
            else
                request.Headers.TryAddWithoutValidation(name, value);
        }

        using var client = httpClients.CreateClient(partner.Connection);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The partner answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    /// <summary>
    /// The MDN of a received message: from its identity (or the AS2 name it was sent to) to the partner, signed
    /// when that was requested and the identity has a signing certificate.
    /// </summary>
    public static As2OutboundMdn BuildMdn(ReceivedMessage message, Identity? identity, string? reason)
    {
        var signing = message.MdnSignedRequested && identity is not null ? As2Certificates.SigningCertificate(identity) : null;
        var disposition = message.MdnDisposition ?? Mdn.ProcessedDisposition;
        return MdnProcessor.Build(new MdnOptions
        {
            From = identity?.As2Id ?? message.As2To,
            To = message.As2From,
            OriginalMessageId = message.MessageId,
            Disposition = disposition,
            ReceivedContentMic = message.Mic,
            Text = reason is null ? null : $"The AS2 message {message.MessageId} could not be processed: {reason}",
            SigningCertificate = signing,
            SignatureAlgorithm = message.MdnMicAlgorithm ?? As2Algorithms.Sha256,
            Subject = $"MDN for {message.MessageId}",
        });
    }
}
