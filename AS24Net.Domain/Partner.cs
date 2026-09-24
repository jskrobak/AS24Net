using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

/// <summary>How the partner is asked to confirm a message.</summary>
public enum MdnMode
{
    /// <summary>No MDN is requested; a message is delivered once the partner accepted the HTTP request.</summary>
    None,

    /// <summary>The MDN comes back in the HTTP response of the message.</summary>
    Sync,

    /// <summary>The partner posts the MDN later to our URL (Receipt-Delivery-Option).</summary>
    Async,
}

/// <summary>A remote AS2 station.</summary>
public class Partner
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>AS2 name of the partner (AS2-To of our messages, AS2-From of its ones).</summary>
    [Required]
    [StringLength(128)]
    public string As2Id { get; set; } = string.Empty;

    /// <summary>Messages are posted to this URL.</summary>
    [Required]
    [StringLength(500)]
    public string Url { get; set; } = string.Empty;

    /// <summary>A disabled partner gets no messages and its messages are refused.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Identity messages are sent from when the queue item names none.</summary>
    public int? DefaultIdentityId { get; set; }
    public Identity? DefaultIdentity { get; set; }

    /// <summary>Media type of the payload when the queue item names none, e.g. application/edifact.</summary>
    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    [StringLength(200)]
    public string? Subject { get; set; }

    public bool SignMessages { get; set; } = true;

    /// <summary>Digest of our signature: sha-256, sha-384, sha-512 or sha1.</summary>
    [Required]
    [StringLength(10)]
    public string SignatureAlgorithm { get; set; } = "sha-256";

    public bool EncryptMessages { get; set; } = true;

    /// <summary>Content encryption: aes256-cbc, aes192-cbc, aes128-cbc or 3des.</summary>
    [Required]
    [StringLength(15)]
    public string EncryptionAlgorithm { get; set; } = "aes256-cbc";

    public bool CompressMessages { get; set; }

    /// <summary>Compress the payload before signing it (RFC 5402 recommends it); otherwise the signed message.</summary>
    public bool CompressBeforeSigning { get; set; } = true;

    public MdnMode MdnMode { get; set; } = MdnMode.Sync;

    /// <summary>Ask for a signed MDN; its MIC is compared with ours.</summary>
    public bool RequestSignedMdn { get; set; } = true;

    /// <summary>Messages of the partner must be signed, otherwise they are refused with insufficient-message-security.</summary>
    public bool RequireSignedMessages { get; set; } = true;

    /// <summary>Messages of the partner must be encrypted.</summary>
    public bool RequireEncryptedMessages { get; set; }

    /// <summary>The partner's certificate its messages and MDNs are signed with.</summary>
    public int? SignatureCertificateId { get; set; }
    public Certificate? SignatureCertificate { get; set; }

    /// <summary>
    /// The partner's signature certificate before the last replacement: messages and MDNs already on their way when
    /// the certificate was replaced may still be signed with it.
    /// </summary>
    public int? PreviousSignatureCertificateId { get; set; }
    public Certificate? PreviousSignatureCertificate { get; set; }

    /// <summary>The partner's certificate our messages are encrypted for (often the same as the signature one).</summary>
    public int? EncryptionCertificateId { get; set; }
    public Certificate? EncryptionCertificate { get; set; }

    /// <summary>
    /// Certificate trusted for the partner's HTTPS server: its own (pinned) certificate or the CA that issued it.
    /// When empty, the system trust store is used.
    /// </summary>
    public int? TlsCertificateId { get; set; }
    public Certificate? TlsCertificate { get; set; }

    /// <summary>User name for HTTP basic authentication at the partner's URL.</summary>
    [StringLength(100)]
    public string? HttpUserName { get; set; }

    /// <summary>Password for HTTP basic authentication, stored encrypted.</summary>
    [StringLength(100)]
    public string? HttpPassword { get; set; }

    /// <summary>Seconds to wait for the answer to a message (with a synchronous MDN, until the MDN arrives).</summary>
    [Range(10, 3600)]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>An asynchronous MDN that did not arrive within this time turns the message into an error.</summary>
    [Range(1, 1440)]
    public int MdnTimeoutMinutes { get; set; } = 120;

    /// <summary>People to call when the connection does not work.</summary>
    public List<PartnerContact> Contacts { get; set; } = [];
}
