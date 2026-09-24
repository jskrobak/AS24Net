using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Logging;
using AS24Net.Core.As2;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.As2;
using AS24Net.Services.Storage;

namespace AS24Net.Services;

/// <summary>A message to be put into the send queue.</summary>
public sealed class QueueRequest
{
    public required int PartnerId { get; init; }

    /// <summary>Our identity to send from; the partner's default identity, or the only one there is, when empty.</summary>
    public int? IdentityId { get; init; }

    /// <summary>The payload, stored on the server already (outbox or elsewhere).</summary>
    public required string FilePath { get; init; }

    /// <summary>File name sent to the partner; the name of the file when empty.</summary>
    public string? FileName { get; init; }

    /// <summary>Media type; the partner's default when empty.</summary>
    public string? ContentType { get; init; }

    public string? Subject { get; init; }
    public string? Reference { get; init; }
    public string? WebhookUrl { get; init; }
    public string? WebhookSecret { get; init; }
}

/// <summary>Puts messages into the send queue, back into it, or out of it.</summary>
public class MessageQueueService(
    IOutgoingMessageRepository messageRepository,
    IPartnerRepository partnerRepository,
    IIdentityRepository identityRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    GlobalSettingsService settingsService,
    OutboxStorage outbox,
    As2SendService sendService,
    ILogger<MessageQueueService> logger)
{
    public async Task<OutgoingMessage> QueueAsync(QueueRequest request, CancellationToken cancellationToken = default)
    {
        var partner = await partnerRepository.GetObjectAsync(request.PartnerId, cancellationToken);
        var identity = await ResolveIdentityAsync(partner, request.IdentityId, cancellationToken);

        var file = new FileInfo(request.FilePath);
        if (!file.Exists)
            throw new InvalidOperationException($"The file {request.FilePath} does not exist.");

        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? partner.ContentType : request.ContentType.Trim();
        if (!contentType.Contains('/'))
            throw new InvalidOperationException($"'{contentType}' is not a media type such as application/edifact.");

        var settings = await settingsService.GetGlobalSettingsAsync();
        var message = new OutgoingMessage
        {
            Created = timeService.GetCurrentTime(),
            PartnerId = partner.Id,
            IdentityId = identity.Id,
            MessageId = As2Headers.NewMessageId(As2Sender.HostOf(settings.PublicUrl)),
            FilePath = file.FullName,
            FileName = Limit(string.IsNullOrWhiteSpace(request.FileName) ? OriginalName(file.Name) : request.FileName.Trim(), 250),
            ContentType = Limit(contentType, 100),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : Limit(request.Subject.Trim(), 200),
            Size = file.Length,
            Status = OutgoingStatus.New,
            MdnMode = partner.MdnMode,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : Limit(request.Reference.Trim(), 100),
            WebhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim(),
            WebhookSecret = string.IsNullOrEmpty(request.WebhookSecret) ? null : request.WebhookSecret,
        };

        unitOfWork.AddForInsert(message);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{FileName} queued for {Partner} as {MessageId}", message.FileName, partner.Name, message.MessageId);

        message.Partner = partner;
        message.Identity = identity;
        sendService.Trigger();
        return message;
    }

    /// <summary>
    /// Puts a failed or finished message back into the queue. It gets a new Message-ID: the partner may have the old
    /// one already and would only confirm it again as a duplicate.
    /// </summary>
    public async Task RequeueAsync(int id, CancellationToken cancellationToken = default)
    {
        var message = await messageRepository.GetObjectAsync(id, cancellationToken);
        var settings = await settingsService.GetGlobalSettingsAsync();
        if (message.Status is OutgoingStatus.Failed or OutgoingStatus.NotDelivered or OutgoingStatus.Delivered or OutgoingStatus.Sent)
            message.MessageId = As2Headers.NewMessageId(As2Sender.HostOf(settings.PublicUrl));

        message.Status = OutgoingStatus.New;
        message.RetryCount = 0;
        message.NextRetry = DateTime.MinValue;
        message.LastError = null;
        message.SentDate = null;
        message.DeliveredDate = null;
        message.MdnDisposition = null;
        message.ReceivedMic = null;
        message.MdnText = null;
        unitOfWork.AddForUpdate(message);
        await unitOfWork.CommitAsync(cancellationToken);
        sendService.Trigger();
    }

    /// <summary>Removes a message from the queue and its payload from the outbox.</summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var message = await messageRepository.GetObjectAsync(id, cancellationToken);
        unitOfWork.AddForDelete(message);
        await unitOfWork.CommitAsync(cancellationToken);
        await outbox.DeleteIfInOutboxAsync(message.FilePath);
    }

    private async Task<Identity> ResolveIdentityAsync(Partner partner, int? identityId, CancellationToken cancellationToken)
    {
        if (identityId is { } id)
            return await identityRepository.GetObjectAsync(id, cancellationToken);
        if (partner.DefaultIdentityId is { } defaultId)
            return await identityRepository.GetObjectAsync(defaultId, cancellationToken);

        var identities = await identityRepository.GetAllAsync(cancellationToken);
        return identities.Count == 1
            ? identities[0]
            : throw new InvalidOperationException(identities.Count == 0
                ? "No identity is configured."
                : $"Partner {partner.Name} has no default identity; name the identity to send from.");
    }

    /// <summary>The name of a file in the outbox without the unique prefix the outbox put in front of it.</summary>
    public static string OriginalName(string storedName)
    {
        var parts = storedName.Split('_', 3);
        return parts.Length == 3 && parts[0].Length == 14 && parts[0].All(char.IsDigit) && parts[1].Length == 32 ? parts[2] : storedName;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
