using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Api;
using AS24Net.Services.Hooks;
using AS24Net.Services.TransferEvents;

namespace AS24Net.Services.Events;

/// <summary>
/// Tells the world what happened to a message: a record in the transfer log, the hook script of the event and the
/// webhooks (of the message, of the API tokens for received messages, and <c>Webhooks:EventsUrl</c> for all events).
/// </summary>
public class As2EventNotifier(
    ITransferEventLog transferEvents,
    IHookDispatcher hooks,
    IWebhookDispatcher webhooks,
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration,
    ILogger<As2EventNotifier> logger)
{
    public const string MessageSentEvent = "message.sent";
    public const string MessageDeliveredEvent = "message.delivered";
    public const string MessageNotDeliveredEvent = "message.not_delivered";
    public const string MessageFailedEvent = "message.failed";
    public const string MessageReceivedEvent = "message.received";
    public const string MessageRefusedEvent = "message.refused";
    public const string CertificateAppliedEvent = "certificate.applied";

    public void Record(TransferEventCategory category, TransferEventType type, TransferEventLevel level, string message,
        Partner? partner, string? partnerName = null, Action<TransferEvent>? configure = null)
    {
        var transferEvent = new TransferEvent
        {
            Category = category,
            Type = type,
            Level = level,
            Message = message,
            PartnerId = partner?.Id,
            PartnerName = partner?.Name ?? partnerName,
        };
        configure?.Invoke(transferEvent);
        transferEvents.Record(transferEvent);
    }

    /// <summary>The partner's server accepted the message.</summary>
    public void MessageSent(OutgoingMessage message, long durationMs)
    {
        Record(TransferEventCategory.Outgoing, TransferEventType.MessageSent, TransferEventLevel.Information,
            $"{message.FileName} sent to {message.Partner.Name} ({Security(message.Signed, message.Encrypted, message.Compressed)})" +
            (message.MdnMode == MdnMode.Async ? ", waiting for the asynchronous MDN" : ""),
            message.Partner, configure: e => Describe(e, message, durationMs));

        hooks.Dispatch(HookEvent.OnSent, Parameters(message));
        Webhook(message, MessageSentEvent);
    }

    public void MessageSendFailed(OutgoingMessage message, string error, bool willRetry, long? durationMs = null)
    {
        Record(TransferEventCategory.Outgoing, TransferEventType.MessageSendFailed,
            willRetry ? TransferEventLevel.Warning : TransferEventLevel.Error,
            willRetry
                ? $"Sending {message.FileName} to {message.Partner.Name} failed, retry {message.RetryCount} at {message.NextRetry:g}: {error}"
                : $"Sending {message.FileName} to {message.Partner.Name} failed for good: {error}",
            message.Partner, configure: e =>
            {
                Describe(e, message, durationMs);
                e.Details = error;
            });

        var parameters = Parameters(message);
        parameters["error"] = error;
        parameters["willRetry"] = willRetry ? "true" : "false";
        hooks.Dispatch(HookEvent.OnSendFailed, parameters);
        if (!willRetry)
            Webhook(message, MessageFailedEvent, error);
    }

    /// <summary>A positive MDN arrived, or the message was accepted and no MDN was requested.</summary>
    public void MessageDelivered(OutgoingMessage message, string? warning = null)
    {
        var mdn = message.MdnMode == MdnMode.None ? "no MDN requested" : $"MDN {message.MdnDisposition}";
        Record(message.MdnMode == MdnMode.None ? TransferEventCategory.Outgoing : TransferEventCategory.Mdn,
            message.MdnMode == MdnMode.None ? TransferEventType.MessageDelivered : TransferEventType.MdnReceived,
            warning is null ? TransferEventLevel.Information : TransferEventLevel.Warning,
            $"{message.FileName} delivered to {message.Partner.Name} ({mdn})" + (warning is null ? "" : $": {warning}"),
            message.Partner, configure: e =>
            {
                Describe(e, message, null);
                e.Details = message.MdnText;
            });

        hooks.Dispatch(HookEvent.OnMdnReceived, Parameters(message));
        Webhook(message, MessageDeliveredEvent);
    }

    /// <summary>A negative or invalid MDN arrived, or none arrived in time.</summary>
    public void MessageNotDelivered(OutgoingMessage message, TransferEventType type, string reason)
    {
        Record(TransferEventCategory.Mdn, type, TransferEventLevel.Error,
            $"{message.FileName} was not delivered to {message.Partner.Name}: {reason}",
            message.Partner, configure: e =>
            {
                Describe(e, message, null);
                e.Details = message.MdnText;
            });

        var parameters = Parameters(message);
        parameters["error"] = reason;
        hooks.Dispatch(HookEvent.OnNotDelivered, parameters);
        Webhook(message, MessageNotDeliveredEvent, reason);
    }

    public void MessageReceived(ReceivedMessage message, Partner partner, bool duplicate)
    {
        Record(TransferEventCategory.Incoming, duplicate ? TransferEventType.DuplicateReceived : TransferEventType.MessageReceived,
            duplicate ? TransferEventLevel.Warning : TransferEventLevel.Information,
            duplicate
                ? $"{message.FileName} received again from {partner.Name} (duplicate of an earlier message, not stored again)"
                : $"{message.FileName} received from {partner.Name} ({Security(message.Signed, message.Encrypted, message.Compressed)})",
            partner, configure: e => Describe(e, message));

        if (duplicate)
            return;

        hooks.Dispatch(HookEvent.OnReceived, Parameters(message));
        var payload = Payload(message, MessageReceivedEvent);
        Global(payload);
        _ = NotifyInboxWebhooksAsync(payload);
    }

    public void MessageRefused(ReceivedMessage message, Partner? partner, string error)
    {
        Record(TransferEventCategory.Incoming, TransferEventType.MessageRefused, TransferEventLevel.Error,
            $"Message {message.MessageId} from {message.As2From} to {message.As2To} refused: {error}",
            partner, message.As2From, e =>
            {
                Describe(e, message);
                e.Details = message.Error;
            });

        var parameters = Parameters(message);
        parameters["error"] = error;
        hooks.Dispatch(HookEvent.OnReceiveFailed, parameters);
        Global(Payload(message, MessageRefusedEvent, error));
    }

    public void MdnSent(ReceivedMessage message, bool async, string? error = null, bool willRetry = false)
    {
        Record(TransferEventCategory.Mdn, error is null ? TransferEventType.MdnSent : TransferEventType.MdnSendFailed,
            error is null ? TransferEventLevel.Information : willRetry ? TransferEventLevel.Warning : TransferEventLevel.Error,
            error is null
                ? $"{(async ? "Asynchronous" : "Synchronous")} MDN for {message.MessageId} sent to {message.As2From}: {message.MdnDisposition}"
                : $"Asynchronous MDN for {message.MessageId} could not be sent to {message.MdnUrl}" +
                  (willRetry ? $", retry {message.MdnRetryCount} at {message.MdnNextRetry:g}" : "") + $": {error}",
            message.Partner, message.As2From, e => Describe(e, message));
    }

    public void CertificateApplied(CertificateChange change, Partner partner)
    {
        Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeApplied, TransferEventLevel.Information,
            $"Certificate {change.Certificate?.Name} is used for {Certificates.CertificateChangeService.Describe(change.Usage)} of partner {partner.Name} from now on",
            partner);

        hooks.Dispatch(HookEvent.OnCertificateApplied, new Dictionary<string, string?>
        {
            ["partnerName"] = partner.Name,
            ["partnerAs2Id"] = partner.As2Id,
            ["certificateId"] = change.CertificateId?.ToString(CultureInfo.InvariantCulture),
            ["certificateName"] = change.Certificate?.Name,
            ["certificateThumbprint"] = change.Certificate?.Thumbprint,
            ["usage"] = change.Usage.ToString(),
            ["activateAt"] = change.ActivateAt.ToString("s", CultureInfo.InvariantCulture),
        });
        Global(new WebhookPayload
        {
            Event = CertificateAppliedEvent,
            PartnerName = partner.Name,
            PartnerAs2Id = partner.As2Id,
            Status = change.Usage.ToString(),
        });
    }

    private static string Security(bool signed, bool encrypted, bool compressed)
    {
        var parts = new List<string>();
        if (signed) parts.Add("signed");
        if (encrypted) parts.Add("encrypted");
        if (compressed) parts.Add("compressed");
        return parts.Count == 0 ? "unsecured" : string.Join(", ", parts);
    }

    private static void Describe(TransferEvent e, OutgoingMessage message, long? durationMs)
    {
        e.MessageId = message.MessageId;
        e.FileName = message.FileName;
        e.FileSize = message.Size;
        e.OutgoingMessageId = message.Id;
        e.DurationMs = durationMs;
        e.RemoteEndPoint = message.Partner?.Url;
    }

    private static void Describe(TransferEvent e, ReceivedMessage message)
    {
        e.MessageId = message.MessageId;
        e.FileName = message.FileName;
        e.FileSize = message.Size;
        e.ReceivedMessageId = message.Id;
        e.RemoteEndPoint = message.RemoteAddress;
    }

    /// <summary>Parameters of the hooks of an outgoing message.</summary>
    public static Dictionary<string, string?> Parameters(OutgoingMessage message) => new()
    {
        ["outgoingMessageId"] = message.Id.ToString(CultureInfo.InvariantCulture),
        ["messageId"] = message.MessageId,
        ["fileName"] = message.FileName,
        ["filePath"] = message.FilePath,
        ["contentType"] = message.ContentType,
        ["size"] = message.Size.ToString(CultureInfo.InvariantCulture),
        ["partnerName"] = message.Partner?.Name,
        ["partnerAs2Id"] = message.Partner?.As2Id,
        ["identityAs2Id"] = message.Identity?.As2Id,
        ["reference"] = message.Reference,
        ["status"] = message.Status.ToString(),
        ["mic"] = message.Mic,
        ["disposition"] = message.MdnDisposition,
    };

    /// <summary>Parameters of the hooks of a received message.</summary>
    public static Dictionary<string, string?> Parameters(ReceivedMessage message) => new()
    {
        ["receivedMessageId"] = message.Id.ToString(CultureInfo.InvariantCulture),
        ["messageId"] = message.MessageId,
        ["fileName"] = message.FileName,
        ["filePath"] = message.FilePath,
        ["contentType"] = message.ContentType,
        ["size"] = message.Size.ToString(CultureInfo.InvariantCulture),
        ["partnerName"] = message.Partner?.Name,
        ["partnerAs2Id"] = message.As2From,
        ["identityAs2Id"] = message.As2To,
        ["subject"] = message.Subject,
        ["signed"] = message.Signed ? "true" : "false",
        ["encrypted"] = message.Encrypted ? "true" : "false",
        ["mic"] = message.Mic,
    };

    private static WebhookPayload Payload(OutgoingMessage message, string eventName, string? error = null) => new()
    {
        Event = eventName,
        OutgoingMessageId = message.Id,
        MessageId = message.MessageId,
        Reference = message.Reference,
        FileName = message.FileName,
        ContentType = message.ContentType,
        Size = message.Size,
        PartnerName = message.Partner?.Name,
        PartnerAs2Id = message.Partner?.As2Id,
        IdentityAs2Id = message.Identity?.As2Id,
        Status = message.Status.ToString(),
        Disposition = message.MdnDisposition,
        Mic = message.Mic,
        Error = error,
        SentDate = message.SentDate,
        DeliveredDate = message.DeliveredDate,
    };

    private static WebhookPayload Payload(ReceivedMessage message, string eventName, string? error = null) => new()
    {
        Event = eventName,
        ReceivedMessageId = message.Id,
        MessageId = message.MessageId,
        FileName = message.FileName,
        ContentType = message.ContentType,
        Size = message.Size,
        PartnerName = message.Partner?.Name,
        PartnerAs2Id = message.As2From,
        IdentityAs2Id = message.As2To,
        Status = message.Status.ToString(),
        Disposition = message.MdnDisposition,
        Mic = message.Mic,
        Error = error,
    };

    private void Webhook(OutgoingMessage message, string eventName, string? error = null)
    {
        var payload = Payload(message, eventName, error);
        if (!string.IsNullOrEmpty(message.WebhookUrl))
            webhooks.Dispatch(message.WebhookUrl, message.WebhookSecret, payload);
        Global(payload);
    }

    /// <summary>The webhook of the whole server, <c>Webhooks:EventsUrl</c>, gets every event.</summary>
    private void Global(WebhookPayload payload)
    {
        if (configuration["Webhooks:EventsUrl"] is { Length: > 0 } url)
            webhooks.Dispatch(url, configuration["Webhooks:EventsSecret"], payload);
    }

    private async Task NotifyInboxWebhooksAsync(WebhookPayload payload)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var tokens = await scope.ServiceProvider.GetRequiredService<IApiTokenRepository>().GetAllAsync();
            foreach (var token in tokens.Where(t => t.Enabled && !string.IsNullOrEmpty(t.InboxWebhookUrl)))
                webhooks.Dispatch(token.InboxWebhookUrl!, token.WebhookSecret, payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notifying the inbox webhooks about message {MessageId} failed", payload.MessageId);
        }
    }
}
