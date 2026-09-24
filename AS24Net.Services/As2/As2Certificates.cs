using System.Security.Cryptography.X509Certificates;
using AS24Net.Domain;
using AS24Net.Services.Certificates;

namespace AS24Net.Services.As2;

/// <summary>The certificates of a partner and of an identity as the protocol needs them.</summary>
public static class As2Certificates
{
    /// <summary>
    /// The certificates the partner's signatures are verified with: the current one and the one before the last
    /// replacement, for messages that were on their way when the certificate changed.
    /// </summary>
    public static List<X509Certificate2> SignatureCertificates(Partner partner) =>
        new[] { partner.SignatureCertificate, partner.PreviousSignatureCertificate }
            .OfType<Certificate>()
            .DistinctBy(c => c.Id)
            .Select(CertificateLoader.Load)
            .ToList();

    /// <summary>
    /// Our certificates a partner may have encrypted for: the decryption certificate, the one before it and the
    /// signing certificate (partners often use the one certificate they know of us for both).
    /// </summary>
    public static List<X509Certificate2> DecryptionCertificates(Identity identity) =>
        new[] { identity.DecryptionCertificate, identity.PreviousDecryptionCertificate, identity.SigningCertificate }
            .OfType<Certificate>()
            .Where(c => c.HasPrivateKey)
            .DistinctBy(c => c.Id)
            .Select(CertificateLoader.Load)
            .ToList();

    public static X509Certificate2? SigningCertificate(Identity identity) =>
        identity.SigningCertificate is { HasPrivateKey: true } certificate ? CertificateLoader.Load(certificate) : null;
}
