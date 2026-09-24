using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

public enum ReceivedStatus
{
    /// <summary>Received and stored.</summary>
    Received,

    /// <summary>Received again (same Message-ID); not stored a second time, the MDN says processed as before.</summary>
    Duplicate,

    /// <summary>Could not be processed (decryption, signature, unknown partner, …); the MDN says why.</summary>
    Failed,
}

public enum MdnDeliveryStatus
{
    /// <summary>The partner did not ask for an MDN.</summary>
    NotRequested,

    /// <summary>Returned in the HTTP response.</summary>
    SentSync,

    /// <summary>To be posted to the partner's URL.</summary>
    Pending,

    /// <summary>Posting failed, retried at <see cref="ReceivedMessage.MdnNextRetry"/>.</summary>
    Retrying,

    /// <summary>Posted to the partner's URL.</summary>
    SentAsync,

    /// <summary>Could not be posted within the retries.</summary>
    Failed,
}

/// <summary>A message received from a partner, including those that failed (for their MDN and the history).</summary>
public class ReceivedMessage
{
    public int Id { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;

    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public int? IdentityId { get; set; }
    public Identity? Identity { get; set; }

    [Required]
    [StringLength(250)]
    public string MessageId { get; set; } = string.Empty;

    [StringLength(128)]
    public string As2From { get; set; } = string.Empty;

    [StringLength(128)]
    public string As2To { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Subject { get; set; }

    [StringLength(250)]
    public string? FileName { get; set; }

    /// <summary>Where the payload is stored; empty when the message failed.</summary>
    [StringLength(2000)]
    public string? FilePath { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public long Size { get; set; }

    public bool Signed { get; set; }
    public bool Encrypted { get; set; }
    public bool Compressed { get; set; }

    public ReceivedStatus Status { get; set; }

    /// <summary>The MIC returned in the MDN.</summary>
    [StringLength(200)]
    public string? Mic { get; set; }

    /// <summary>Disposition returned in the MDN.</summary>
    [StringLength(500)]
    public string? MdnDisposition { get; set; }

    public MdnDeliveryStatus MdnStatus { get; set; }

    /// <summary>URL the asynchronous MDN goes to.</summary>
    [StringLength(500)]
    public string? MdnUrl { get; set; }

    public bool MdnSignedRequested { get; set; }

    [StringLength(10)]
    public string? MdnMicAlgorithm { get; set; }

    public int MdnRetryCount { get; set; }
    public DateTime? MdnNextRetry { get; set; }
    public DateTime? MdnSentDate { get; set; }

    [StringLength(2000)]
    public string? MdnLastError { get; set; }

    [StringLength(2000)]
    public string? Error { get; set; }

    [StringLength(100)]
    public string? RemoteAddress { get; set; }

    /// <summary>When the file was fetched through the REST API.</summary>
    public DateTime? FetchedDate { get; set; }
}
