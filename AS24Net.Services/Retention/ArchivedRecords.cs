using AS24Net.Domain;

namespace AS24Net.Services.Retention;

/// <summary>
/// A sent message as it is written to the archive: with the names of its partner and identity, which may be gone
/// by the time somebody reads it, and without the webhook secret.
/// </summary>
public sealed record ArchivedOutgoingMessage(
    int Id, DateTime Created, string? Partner, string? PartnerAs2Id, string? IdentityAs2Id, string MessageId,
    string FileName, string FilePath, string ContentType, string? Subject, long Size, OutgoingStatus Status,
    DateTime? SentDate, DateTime? DeliveredDate, int RetryCount, string? LastError, DateTime? LastErrorDate,
    MdnMode MdnMode, string? Mic, string? ReceivedMic, string? MdnDisposition, string? MdnText, bool MdnSigned,
    string? MdnMessageId, bool Signed, bool Encrypted, bool Compressed, string? Reference, string? WebhookUrl)
{
    public static ArchivedOutgoingMessage From(OutgoingMessage m) => new(
        m.Id, m.Created, m.Partner?.Name, m.Partner?.As2Id, m.Identity?.As2Id, m.MessageId,
        m.FileName, m.FilePath, m.ContentType, m.Subject, m.Size, m.Status,
        m.SentDate, m.DeliveredDate, m.RetryCount, m.LastError, m.LastErrorDate,
        m.MdnMode, m.Mic, m.ReceivedMic, m.MdnDisposition, m.MdnText, m.MdnSigned,
        m.MdnMessageId, m.Signed, m.Encrypted, m.Compressed, m.Reference, m.WebhookUrl);
}

/// <summary>A received message as it is written to the archive, with the name of its partner.</summary>
public sealed record ArchivedReceivedMessage(
    int Id, DateTime Created, string? Partner, string MessageId, string As2From, string As2To, string? Subject,
    string? FileName, string? FilePath, string? ContentType, long Size, bool Signed, bool Encrypted, bool Compressed,
    ReceivedStatus Status, string? Mic, string? MdnDisposition, MdnDeliveryStatus MdnStatus, string? MdnUrl,
    DateTime? MdnSentDate, string? MdnLastError, string? Error, string? RemoteAddress, DateTime? FetchedDate)
{
    public static ArchivedReceivedMessage From(ReceivedMessage m) => new(
        m.Id, m.Created, m.Partner?.Name, m.MessageId, m.As2From, m.As2To, m.Subject,
        m.FileName, m.FilePath, m.ContentType, m.Size, m.Signed, m.Encrypted, m.Compressed,
        m.Status, m.Mic, m.MdnDisposition, m.MdnStatus, m.MdnUrl,
        m.MdnSentDate, m.MdnLastError, m.Error, m.RemoteAddress, m.FetchedDate);
}
