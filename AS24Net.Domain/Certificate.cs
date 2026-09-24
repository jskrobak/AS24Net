using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

/// <summary>
/// A stored certificate: ours with the private key (PKCS#12, for signing and decryption) or a partner's without it.
/// </summary>
public class Certificate
{
    public int Id { get; set; }

    [Required]
    [StringLength(500)]
    public string Name { get; set; } = string.Empty;

    /// <summary>PKCS#12 with the private key, otherwise the DER or PEM of the certificate, in base64.</summary>
    public string? Base64Data { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool HasPrivateKey { get; set; }

    /// <summary>SHA-1 thumbprint, hex, to recognise the certificate.</summary>
    [StringLength(40)]
    public string? Thumbprint { get; set; }

    /// <summary>Password of the PKCS#12, stored encrypted.</summary>
    [StringLength(50)]
    public string Password { get; set; } = string.Empty;

    public DateTime Created { get; set; } = DateTime.Now;
}
