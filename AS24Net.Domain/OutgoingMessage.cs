using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

public enum OutgoingStatus
{
    /// <summary>Waiting to be sent (also after a failed attempt that will be retried at NextRetry).</summary>
    New = 0,

    /// <summary>Sending failed, will be retried at <see cref="OutgoingMessage.NextRetry"/>.</summary>
    Error = 42,

    /// <summary>Failed for good: retry limit reached or refused by the partner.</summary>
    Failed = 43,

    /// <summary>The partner returned a negative MDN (processed/error, failed).</summary>
    NotDelivered = 44,

    /// <summary>The partner accepted the message; waiting for its asynchronous MDN.</summary>
    Sent = 999,

    /// <summary>Confirmed by a positive MDN, or accepted by the partner when no MDN was requested.</summary>
    Delivered = 1000,
}

/// <summary>A message in the send queue.</summary>
public class OutgoingMessage
{
    public int Id { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;

    public int PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;
    public int IdentityId { get; set; }
    public Identity Identity { get; set; } = null!;

    /// <summary>Message-ID, created when the message is queued so that retries keep it (the partner recognises duplicates by it).</summary>
    [Required]
    [StringLength(250)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>File name sent in Content-Disposition.</summary>
    [Required]
    [StringLength(250)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    [StringLength(200)]
    public string? Subject { get; set; }

    public long Size { get; set; }

    public OutgoingStatus Status { get; set; }

    public int RetryCount { get; set; }
    public DateTime NextRetry { get; set; } = DateTime.MinValue;

    [StringLength(2000)]
    public string? LastError { get; set; }

    public DateTime? LastErrorDate { get; set; }

    public DateTime? SentDate { get; set; }

    /// <summary>When the MDN arrived (or the message was accepted without an MDN).</summary>
    public DateTime? DeliveredDate { get; set; }

    /// <summary>How the MDN was requested for the last transfer.</summary>
    public MdnMode MdnMode { get; set; }

    /// <summary>The MIC we computed; the MDN has to return the same one.</summary>
    [StringLength(200)]
    public string? Mic { get; set; }

    /// <summary>The MIC in the MDN.</summary>
    [StringLength(200)]
    public string? ReceivedMic { get; set; }

    /// <summary>Disposition in the MDN.</summary>
    [StringLength(500)]
    public string? MdnDisposition { get; set; }

    /// <summary>Human readable text of the MDN.</summary>
    [StringLength(2000)]
    public string? MdnText { get; set; }

    public bool MdnSigned { get; set; }

    [StringLength(250)]
    public string? MdnMessageId { get; set; }

    public bool Signed { get; set; }
    public bool Encrypted { get; set; }
    public bool Compressed { get; set; }

    /// <summary>Identification of the message in the calling system (REST API), for correlation.</summary>
    [StringLength(100)]
    public string? Reference { get; set; }

    /// <summary>URL notified about the progress of this message (REST API).</summary>
    [StringLength(500)]
    public string? WebhookUrl { get; set; }

    /// <summary>Secret used to sign the webhook request, stored encrypted.</summary>
    [StringLength(200)]
    public string? WebhookSecret { get; set; }
}
