using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

public enum TransferEventCategory
{
    /// <summary>Messages we send.</summary>
    Outgoing,

    /// <summary>Messages partners send to us.</summary>
    Incoming,

    /// <summary>MDNs in both directions.</summary>
    Mdn,

    /// <summary>Runs of event hook scripts and webhook calls.</summary>
    Hook,

    /// <summary>Scheduled certificate changes of partners.</summary>
    Certificate,
}

public enum TransferEventLevel
{
    Information,
    Warning,
    Error,
}

public enum TransferEventType
{
    MessageSent,
    MessageSendFailed,
    MessageDelivered,
    MessageNotDelivered,
    MessageReceived,
    MessageRefused,
    DuplicateReceived,
    MdnSent,
    MdnSendFailed,
    MdnSuppressed,
    MdnReceived,
    MdnInvalid,
    MdnTimedOut,
    MicMismatch,
    HookFinished,
    HookFailed,
    WebhookDelivered,
    WebhookFailed,
    CertificateChangeScheduled,
    CertificateChangeApplied,
    CertificateChangeCancelled,
    CertificateChangeFailed,

    /// <summary>A connection test: the partner's endpoint was reached, nothing was sent.</summary>
    ConnectionTested,
}

/// <summary>
/// Audit record of the transfer history shown in the Logs section. Records older than the archive period are marked
/// as archived; they are kept but hidden unless the complete archive is requested.
/// </summary>
/// <remarks>Partner and message data are copied, so the history survives deleting partners or queue items.</remarks>
public class TransferEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public TransferEventCategory Category { get; set; }
    public TransferEventLevel Level { get; set; }
    public TransferEventType Type { get; set; }

    public int? PartnerId { get; set; }

    [StringLength(50)]
    public string? PartnerName { get; set; }

    [StringLength(100)]
    public string? RemoteEndPoint { get; set; }

    /// <summary>Message-ID of the AS2 message.</summary>
    [StringLength(250)]
    public string? MessageId { get; set; }

    [StringLength(250)]
    public string? FileName { get; set; }

    public long? FileSize { get; set; }
    public int? OutgoingMessageId { get; set; }
    public int? ReceivedMessageId { get; set; }

    [StringLength(1000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Additional text: error details, hook output, …</summary>
    public string? Details { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>Parameters a hook script was run with (JSON as on its standard input), so it can be run again.</summary>
    public string? HookParameters { get; set; }

    public bool IsArchived { get; set; }
}
