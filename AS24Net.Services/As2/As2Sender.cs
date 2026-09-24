using System.Diagnostics;
using System.Net;
using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Logging;
using AS24Net.Core.As2;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;

namespace AS24Net.Services.As2;

/// <summary>A transfer failed; <see cref="Retry"/> tells whether trying again later may help.</summary>
public class As2TransferException(string message, bool retry, Exception? innerException = null) : Exception(message, innerException)
{
    public bool Retry { get; } = retry;
}

/// <summary>
/// Sends one message of the send queue: builds it as the partner is configured (compressed, signed, encrypted),
/// posts it and processes the synchronous MDN, or leaves the message waiting for the asynchronous one.
/// </summary>
public class As2Sender(
    IOutgoingMessageRepository messageRepository,
    IPartnerRepository partnerRepository,
    IIdentityRepository identityRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    As2HttpClientProvider httpClients,
    As2EventNotifier notifier,
    ILogger<As2Sender> logger)
{
    /// <summary>Sends the message; failures are recorded in it (with the next retry) rather than thrown.</summary>
    public async Task SendAsync(int messageId, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var message = await messageRepository.FindWithRefsAsync(messageId, cancellationToken)
                      ?? throw new InvalidOperationException($"Message {messageId} does not exist.");
        if (message.Status is not (OutgoingStatus.New or OutgoingStatus.Error))
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var outcome = await TransferAsync(message, settings, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            notifier.MessageSent(message, stopwatch.ElapsedMilliseconds);
            if (message.Status == OutgoingStatus.Delivered)
                notifier.MessageDelivered(message, outcome?.Warning);
            else if (outcome is { Delivered: false })
                notifier.MessageNotDelivered(message, outcome.FailureType ?? TransferEventType.MessageNotDelivered, outcome.Problem ?? "negative MDN");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var retry = ex is not As2TransferException { Retry: false };
            var error = ex is As2TransferException or HttpRequestException or TaskCanceledException ? ex.Message : ex.ToString();
            await MarkFailedAsync(message, error, retry, settings, cancellationToken);
            notifier.MessageSendFailed(message, error, message.Status == OutgoingStatus.Error, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <returns>What the synchronous MDN means, <c>null</c> without one.</returns>
    private async Task<MdnOutcome?> TransferAsync(OutgoingMessage message, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var partner = await partnerRepository.FindWithRefsAsync(message.PartnerId, cancellationToken)
                      ?? throw new As2TransferException("The partner does not exist any more.", retry: false);
        var identity = await identityRepository.FindWithRefsAsync(message.IdentityId, cancellationToken)
                       ?? throw new As2TransferException("The identity does not exist any more.", retry: false);
        if (!partner.Enabled)
            throw new As2TransferException($"Partner {partner.Name} is disabled.", retry: true);

        if (!File.Exists(message.FilePath))
            throw new As2TransferException($"The file {message.FilePath} does not exist.", retry: false);

        var payload = await File.ReadAllBytesAsync(message.FilePath, cancellationToken);
        var options = CreateOptions(message, partner, identity, payload, settings);
        var outbound = As2MessageBuilder.Build(options);

        logger.LogInformation("Sending {FileName} ({Size} bytes) to {Partner} at {Url} as {MessageId}",
            message.FileName, payload.Length, partner.Name, partner.Url, outbound.MessageId);

        using var request = new HttpRequestMessage(HttpMethod.Post, partner.Url);
        request.Content = new ByteArrayContent(outbound.Body);
        foreach (var (name, value) in outbound.Headers)
        {
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                request.Content.Headers.TryAddWithoutValidation(name, value);
            else
                request.Headers.TryAddWithoutValidation(name, value);
        }

        using var client = httpClients.CreateClient(partner);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new As2TransferException($"The partner did not answer within {partner.TimeoutSeconds} s.", retry: true, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new As2TransferException($"The partner answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}" +
                                               Excerpt(body), retry: IsRetryable(response.StatusCode));

            var now = timeService.GetCurrentTime();
            message.SentDate = now;
            message.Mic = outbound.Mic;
            message.Signed = outbound.Signed;
            message.Encrypted = outbound.Encrypted;
            message.Compressed = outbound.Compressed;
            message.MdnMode = partner.MdnMode;
            message.LastError = null;
            message.Partner = partner;
            message.Identity = identity;
            MdnOutcome? outcome = null;

            switch (partner.MdnMode)
            {
                case MdnMode.None:
                    message.Status = OutgoingStatus.Delivered;
                    message.DeliveredDate = now;
                    break;

                case MdnMode.Async:
                    message.Status = OutgoingStatus.Sent;
                    break;

                default:
                    var headers = As2Headers.NewCollection();
                    foreach (var header in response.Headers.Concat(response.Content.Headers))
                        headers[header.Key] = string.Join(", ", header.Value);

                    if (body.Length == 0 || !MdnProcessor.IsMdn(headers, body))
                        throw new As2TransferException("The partner accepted the message but returned no MDN" + Excerpt(body), retry: true);

                    Mdn mdn;
                    try
                    {
                        mdn = MdnProcessor.Read(headers, body, As2Certificates.SignatureCertificates(partner), partner.RequestSignedMdn);
                    }
                    catch (As2ProcessingException ex)
                    {
                        // The partner has the message, but its MDN cannot be trusted: sending it again would not help.
                        throw new As2TransferException("The MDN of the partner is not valid: " + ex.Message, retry: false, ex);
                    }

                    if (mdn.OriginalMessageId is { } original && original != message.MessageId)
                        throw new As2TransferException($"The MDN is about message {original}, not {message.MessageId}.", retry: false);

                    outcome = MdnEvaluation.Apply(message, mdn, now);
                    break;
            }

            unitOfWork.AddForUpdate(message);
            return outcome;
        }
    }

    private static As2OutboundOptions CreateOptions(OutgoingMessage message, Partner partner, Identity identity, byte[] payload,
        GlobalSettings settings)
    {
        var signing = partner.SignMessages
            ? As2Certificates.SigningCertificate(identity)
              ?? throw new As2TransferException($"Identity {identity.Name} has no signing certificate with a private key.", retry: false)
            : null;
        var encryption = partner.EncryptMessages
            ? partner.EncryptionCertificate is { } certificate
                ? Certificates.CertificateLoader.Load(certificate)
                : throw new As2TransferException($"Partner {partner.Name} has no encryption certificate.", retry: false)
            : null;

        MdnRequest? mdn = null;
        if (partner.MdnMode != MdnMode.None)
        {
            if (partner.MdnMode == MdnMode.Async && string.IsNullOrWhiteSpace(settings.PublicUrl))
                throw new As2TransferException("An asynchronous MDN needs the public URL of this server (Settings).", retry: false);

            mdn = new MdnRequest
            {
                NotificationTo = string.IsNullOrWhiteSpace(identity.Email) ? identity.As2Id : identity.Email,
                ReturnUrl = partner.MdnMode == MdnMode.Async ? settings.PublicUrl : null,
                Signed = partner.RequestSignedMdn,
                MicAlgorithms = [partner.SignatureAlgorithm],
            };
        }

        return new As2OutboundOptions
        {
            From = identity.As2Id,
            To = partner.As2Id,
            MessageId = message.MessageId,
            Subject = message.Subject ?? partner.Subject ?? message.FileName,
            Payload = payload,
            ContentType = message.ContentType,
            FileName = message.FileName,
            Compress = partner.CompressMessages,
            CompressBeforeSigning = partner.CompressBeforeSigning,
            SigningCertificate = signing,
            SignatureAlgorithm = partner.SignatureAlgorithm,
            EncryptionCertificate = encryption,
            EncryptionAlgorithm = partner.EncryptionAlgorithm,
            Mdn = mdn,
            Host = HostOf(settings.PublicUrl),
        };
    }

    private async Task MarkFailedAsync(OutgoingMessage message, string error, bool retry, GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        var now = timeService.GetCurrentTime();
        message.LastError = MdnEvaluation.Truncate(error, 2000);
        message.LastErrorDate = now;
        message.RetryCount++;

        if (retry && message.RetryCount <= settings.MaxRetryCount)
        {
            message.Status = OutgoingStatus.Error;
            message.NextRetry = now.Add(RetryDelay(message.RetryCount, settings));
            logger.LogWarning("Sending {FileName} to {Partner} failed, retry {Retry} at {NextRetry}: {Error}",
                message.FileName, message.Partner?.Name, message.RetryCount, message.NextRetry, error);
        }
        else
        {
            message.Status = OutgoingStatus.Failed;
            logger.LogError("Sending {FileName} to {Partner} failed for good: {Error}", message.FileName, message.Partner?.Name, error);
        }

        unitOfWork.AddForUpdate(message);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>Exponential back-off: the first delay, twice as long for every further retry, at most the maximum.</summary>
    public static TimeSpan RetryDelay(int retryCount, GlobalSettings settings) =>
        TimeSpan.FromMinutes(Math.Min(settings.RetryDelayMinutes * Math.Pow(2, Math.Max(0, retryCount - 1)), settings.MaxRetryDelayMinutes));

    /// <summary>Client errors that will not go away by themselves are not retried; everything else is.</summary>
    internal static bool IsRetryable(HttpStatusCode status) => status is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound
        or HttpStatusCode.MethodNotAllowed or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType);

    private static string Excerpt(byte[] body)
    {
        if (body.Length == 0)
            return ".";
        var text = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300)).ReplaceLineEndings(" ").Trim();
        return $": {text}";
    }

    internal static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
}
