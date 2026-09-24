using System.Text;
using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Logging;
using AS24Net.Core.As2;
using AS24Net.Core.Security;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;
using AS24Net.Services.Storage;

namespace AS24Net.Services.As2;

/// <summary>The HTTP answer to a request of a partner.</summary>
public sealed class As2Response
{
    public int StatusCode { get; init; } = 200;
    public Dictionary<string, string> Headers { get; init; } = As2Headers.NewCollection();
    public byte[] Body { get; init; } = [];

    public static As2Response Text(int statusCode, string text)
    {
        var headers = As2Headers.NewCollection();
        headers[As2Headers.ContentType] = "text/plain; charset=utf-8";
        return new As2Response { StatusCode = statusCode, Headers = headers, Body = Encoding.UTF8.GetBytes(text + "\r\n") };
    }
}

/// <summary>
/// Processes what partners post to the AS2 endpoint: messages (stored, confirmed with a synchronous MDN in the
/// response or an asynchronous one later) and asynchronous MDNs of our messages.
/// </summary>
public class As2InboundService(
    IPartnerRepository partnerRepository,
    IIdentityRepository identityRepository,
    IReceivedMessageRepository receivedRepository,
    IOutgoingMessageRepository outgoingRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    InboxStorage inbox,
    As2EventNotifier notifier,
    AsyncMdnService asyncMdns,
    ILogger<As2InboundService> logger)
{
    public async Task<As2Response> HandleAsync(IReadOnlyDictionary<string, string> headers, byte[] body, string? remoteAddress,
        CancellationToken cancellationToken)
    {
        if (MdnProcessor.IsMdn(headers, body))
            return await HandleMdnAsync(headers, body, remoteAddress, cancellationToken);

        return await HandleMessageAsync(headers, body, remoteAddress, cancellationToken);
    }

    #region Messages

    private async Task<As2Response> HandleMessageAsync(IReadOnlyDictionary<string, string> headers, byte[] body, string? remoteAddress,
        CancellationToken cancellationToken)
    {
        var from = As2Headers.UnquoteAs2Id(headers.GetValueOrDefault(As2Headers.As2From));
        var to = As2Headers.UnquoteAs2Id(headers.GetValueOrDefault(As2Headers.As2To));
        var messageId = As2Headers.NormalizeMessageId(headers.GetValueOrDefault(As2Headers.MessageId));
        if (from.Length == 0 || to.Length == 0 || messageId.Length == 0)
            return As2Response.Text(400, "An AS2 message needs the headers AS2-From, AS2-To and Message-ID.");

        var mdnRequest = MdnRequest.FromHeaders(headers);
        var partner = await partnerRepository.FindByAs2IdAsync(from, cancellationToken);
        var identity = await identityRepository.FindByAs2IdAsync(to, cancellationToken);
        var now = timeService.GetCurrentTime();

        var record = new ReceivedMessage
        {
            Created = now,
            PartnerId = partner?.Id,
            Partner = partner,
            IdentityId = identity?.Id,
            Identity = identity,
            MessageId = MdnEvaluation.Truncate(messageId, 250)!,
            As2From = MdnEvaluation.Truncate(from, 128)!,
            As2To = MdnEvaluation.Truncate(to, 128)!,
            Subject = MdnEvaluation.Truncate(headers.GetValueOrDefault(As2Headers.Subject), 200),
            RemoteAddress = remoteAddress,
            MdnSignedRequested = mdnRequest?.Signed ?? false,
            MdnStatus = MdnDeliveryStatus.NotRequested,
        };

        if (partner is null || identity is null || !partner.Enabled)
        {
            var reason = partner is null ? $"The partner {from} is not known here."
                : identity is null ? $"{to} is not an AS2 name of this server."
                : $"Partner {partner.Name} is disabled.";
            // Nobody to trust: no asynchronous MDN to an address from the request, only an answer to the request.
            return await RefuseAsync(record, partner, identity, As2Errors.UnknownTradingPartner, reason, mdnRequest,
                allowAsync: false, cancellationToken);
        }

        // A message received before (AS2 reliability): confirmed again, not stored again.
        if (await receivedRepository.FindReceivedAsync(record.As2From, record.MessageId, cancellationToken) is { } earlier)
        {
            record.Status = ReceivedStatus.Duplicate;
            record.FileName = earlier.FileName;
            record.FilePath = earlier.FilePath;
            record.ContentType = earlier.ContentType;
            record.Size = earlier.Size;
            record.Signed = earlier.Signed;
            record.Encrypted = earlier.Encrypted;
            record.Compressed = earlier.Compressed;
            record.Mic = earlier.Mic;
            record.MdnMicAlgorithm = earlier.MdnMicAlgorithm;
            logger.LogWarning("Message {MessageId} of {Partner} was received before (#{Id}), it is not stored again",
                messageId, partner.Name, earlier.Id);
            var duplicate = await ConfirmAsync(record, partner, identity, Mdn.WarningDisposition("duplicate-document"), mdnRequest, cancellationToken);
            notifier.MessageReceived(record, partner, duplicate: true);
            return duplicate;
        }

        As2InboundMessage inbound;
        try
        {
            inbound = As2MessageReader.Read(headers, body, As2Certificates.DecryptionCertificates(identity),
                As2Certificates.SignatureCertificates(partner), partner.SignatureAlgorithm);
        }
        catch (As2ProcessingException ex)
        {
            return await RefuseAsync(record, partner, identity, ex.Error, ex.Message, mdnRequest, allowAsync: true, cancellationToken);
        }

        record.Signed = inbound.Signed;
        record.Encrypted = inbound.Encrypted;
        record.Compressed = inbound.Compressed;
        record.Mic = inbound.Mic;
        record.MdnMicAlgorithm = Mic.Parse(inbound.Mic)?.Algorithm;
        record.ContentType = MdnEvaluation.Truncate(inbound.ContentType, 100);

        if (partner.RequireSignedMessages && !inbound.Signed || partner.RequireEncryptedMessages && !inbound.Encrypted)
        {
            var missing = partner.RequireSignedMessages && !inbound.Signed ? "signed" : "encrypted";
            return await RefuseAsync(record, partner, identity, As2Errors.InsufficientMessageSecurity,
                $"Messages of partner {partner.Name} have to be {missing}.", mdnRequest, allowAsync: true, cancellationToken);
        }

        var fileName = string.IsNullOrWhiteSpace(inbound.FileName) ? DefaultFileName(messageId, inbound.ContentType) : inbound.FileName.Trim();
        try
        {
            record.FilePath = await inbox.SaveAsync(partner.As2Id, fileName, inbound.Payload, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The payload of message {MessageId} cannot be stored", messageId);
            return await RefuseAsync(record, partner, identity, As2Errors.UnexpectedProcessingError,
                "The message cannot be stored: " + ex.Message, mdnRequest, allowAsync: true, cancellationToken);
        }

        record.FileName = MdnEvaluation.Truncate(fileName, 250);
        record.Size = inbound.Payload.Length;
        record.Status = ReceivedStatus.Received;
        logger.LogInformation("Message {MessageId} ({FileName}, {Size} bytes) received from {Partner} for {Identity}",
            messageId, fileName, record.Size, partner.Name, identity.Name);

        var response = await ConfirmAsync(record, partner, identity, Mdn.ProcessedDisposition, mdnRequest, cancellationToken);
        notifier.MessageReceived(record, partner, duplicate: false);
        return response;
    }

    private async Task<As2Response> RefuseAsync(ReceivedMessage record, Partner? partner, Identity? identity, string error, string reason,
        MdnRequest? mdnRequest, bool allowAsync, CancellationToken cancellationToken)
    {
        logger.LogWarning("Message {MessageId} from {From} to {To} refused ({Error}): {Reason}",
            record.MessageId, record.As2From, record.As2To, error, reason);
        record.Status = ReceivedStatus.Failed;
        record.Error = MdnEvaluation.Truncate(reason, 2000);

        As2Response response;
        if (mdnRequest is { IsAsync: true } && !allowAsync)
        {
            record.MdnDisposition = Mdn.ErrorDisposition(error);
            unitOfWork.AddForInsert(record);
            await unitOfWork.CommitAsync(cancellationToken);
            response = As2Response.Text(403, reason);
        }
        else
        {
            response = await ConfirmAsync(record, partner, identity, Mdn.ErrorDisposition(error), mdnRequest, cancellationToken, reason);
        }

        notifier.MessageRefused(record, partner, $"{error}: {reason}");
        return response;
    }

    /// <summary>Stores the record and answers with the MDN (synchronous), or queues it (asynchronous).</summary>
    private async Task<As2Response> ConfirmAsync(ReceivedMessage record, Partner? partner, Identity? identity, string disposition,
        MdnRequest? mdnRequest, CancellationToken cancellationToken, string? reason = null)
    {
        record.MdnDisposition = MdnEvaluation.Truncate(disposition, 500);

        if (mdnRequest is null)
        {
            unitOfWork.AddForInsert(record);
            await unitOfWork.CommitAsync(cancellationToken);
            return record.Status == ReceivedStatus.Failed
                ? As2Response.Text(400, $"The message was not processed: {reason}")
                : As2Response.Text(200, "The message was received.");
        }

        if (mdnRequest.IsAsync)
        {
            record.MdnStatus = MdnDeliveryStatus.Pending;
            record.MdnUrl = MdnEvaluation.Truncate(mdnRequest.ReturnUrl, 500);
            unitOfWork.AddForInsert(record);
            await unitOfWork.CommitAsync(cancellationToken);
            asyncMdns.Trigger();
            return As2Response.Text(200, "The message was received, its MDN follows asynchronously.");
        }

        var mdn = AsyncMdnService.BuildMdn(record, identity, reason);
        record.MdnStatus = MdnDeliveryStatus.SentSync;
        record.MdnSentDate = timeService.GetCurrentTime();
        unitOfWork.AddForInsert(record);
        await unitOfWork.CommitAsync(cancellationToken);
        notifier.MdnSent(record, async: false);

        return new As2Response { StatusCode = 200, Headers = mdn.Headers, Body = mdn.Body };
    }

    private static string DefaultFileName(string messageId, string contentType)
    {
        var name = new string(messageId.Trim('<', '>').Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray());
        var extension = contentType.ToLowerInvariant() switch
        {
            "application/edifact" => ".edi",
            "application/edi-x12" => ".x12",
            "application/xml" or "text/xml" => ".xml",
            "application/json" => ".json",
            "text/plain" => ".txt",
            "application/pdf" => ".pdf",
            _ => ".bin",
        };
        return (name.Length > 100 ? name[..100] : name) + extension;
    }

    #endregion

    #region Asynchronous MDNs

    private async Task<As2Response> HandleMdnAsync(IReadOnlyDictionary<string, string> headers, byte[] body, string? remoteAddress,
        CancellationToken cancellationToken)
    {
        var from = As2Headers.UnquoteAs2Id(headers.GetValueOrDefault(As2Headers.As2From));
        var partner = from.Length == 0 ? null : await partnerRepository.FindByAs2IdAsync(from, cancellationToken);
        if (partner is null)
        {
            logger.LogWarning("MDN from unknown partner '{From}' ({RemoteAddress}) refused", from, remoteAddress);
            return As2Response.Text(403, $"The partner '{from}' is not known here.");
        }

        Mdn mdn;
        try
        {
            mdn = MdnProcessor.Read(headers, body, As2Certificates.SignatureCertificates(partner), requireSignature: false);
        }
        catch (As2ProcessingException ex)
        {
            notifier.Record(TransferEventCategory.Mdn, TransferEventType.MdnInvalid, TransferEventLevel.Error,
                $"Invalid MDN from {partner.Name}: {ex.Message}", partner, configure: e => e.RemoteEndPoint = remoteAddress);
            return As2Response.Text(400, "The MDN is not valid: " + ex.Message);
        }

        var message = mdn.OriginalMessageId is null ? null : await outgoingRepository.FindByMessageIdAsync(mdn.OriginalMessageId, cancellationToken);
        if (message is null || message.PartnerId != partner.Id)
        {
            notifier.Record(TransferEventCategory.Mdn, TransferEventType.MdnInvalid, TransferEventLevel.Warning,
                $"MDN from {partner.Name} for unknown message {mdn.OriginalMessageId}", partner,
                configure: e => e.MessageId = mdn.OriginalMessageId);
            return As2Response.Text(404, $"The message {mdn.OriginalMessageId} is not known here.");
        }

        if (message.Status is OutgoingStatus.Delivered or OutgoingStatus.NotDelivered)
        {
            logger.LogInformation("MDN for {MessageId} received again, the message is {Status} already", message.MessageId, message.Status);
            return As2Response.Text(200, "The MDN was received before.");
        }

        if (partner.RequestSignedMdn && !mdn.Signed)
        {
            message.Status = OutgoingStatus.NotDelivered;
            message.LastError = "A signed MDN was requested, but the MDN is not signed.";
            message.LastErrorDate = timeService.GetCurrentTime();
            message.MdnDisposition = MdnEvaluation.Truncate(mdn.Disposition, 500);
            unitOfWork.AddForUpdate(message);
            await unitOfWork.CommitAsync(cancellationToken);
            notifier.MessageNotDelivered(message, TransferEventType.MdnInvalid, message.LastError);
            return As2Response.Text(400, message.LastError);
        }

        var outcome = MdnEvaluation.Apply(message, mdn, timeService.GetCurrentTime());
        unitOfWork.AddForUpdate(message);
        await unitOfWork.CommitAsync(cancellationToken);

        if (outcome.Delivered)
            notifier.MessageDelivered(message, outcome.Warning);
        else
            notifier.MessageNotDelivered(message, outcome.FailureType ?? TransferEventType.MessageNotDelivered, outcome.Problem ?? "negative MDN");

        return As2Response.Text(200, "The MDN was received.");
    }

    #endregion
}
