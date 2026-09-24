using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

/// <summary>
/// Our own AS2 station: the AS2 name partners send to (AS2-To) and that we send from (AS2-From), with the
/// certificates we sign messages and MDNs with and that partners encrypt for.
/// </summary>
public class Identity
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>AS2 name (AS2-From of our messages, AS2-To of the partners' ones), 1 - 128 printable characters.</summary>
    [Required]
    [StringLength(128)]
    public string As2Id { get; set; } = string.Empty;

    /// <summary>E-mail address sent in Disposition-Notification-To when an MDN is requested.</summary>
    [StringLength(200)]
    public string? Email { get; set; }

    /// <summary>Our certificate with private key our messages and MDNs are signed with.</summary>
    public int? SigningCertificateId { get; set; }
    public Certificate? SigningCertificate { get; set; }

    /// <summary>Our certificate with private key partners encrypt messages for.</summary>
    public int? DecryptionCertificateId { get; set; }
    public Certificate? DecryptionCertificate { get; set; }

    /// <summary>
    /// The certificate partners encrypted for before the last replacement: messages already on their way may
    /// still be encrypted for it.
    /// </summary>
    public int? PreviousDecryptionCertificateId { get; set; }
    public Certificate? PreviousDecryptionCertificate { get; set; }
}
