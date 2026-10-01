using System.Security.Cryptography.X509Certificates;
using AS24Net.Domain;
using AS24Net.Services.Certificates;

namespace AS24Net.Services.As2;

/// <summary>The certificates of a partner (its connection) and of an identity as the protocol needs them.</summary>
public static class As2Certificates
{
    /// <summary>
    /// The certificates the partner's signatures are verified with (those of its connection): the current one and the
    /// one before the last replacement, for messages that were on their way when the certificate changed.
    /// </summary>
    public static List<X509Certificate2> SignatureCertificates(Partner partner) =>
        new[] { partner.Connection.SignatureCertificate, partner.Connection.PreviousSignatureCertificate }
            .OfType<Certificate>()
            .DistinctBy(c => c.Id)
            .Select(CertificateLoader.Load)
            .ToList();

    /// <summary>
    /// Our certificates a partner may have encrypted for: our own certificate of the partner, when it has one, then
    /// the decryption certificate of the identity, the one before it and the signing certificate (partners often use
    /// the one certificate they know of us for both).
    /// </summary>
    public static List<X509Certificate2> DecryptionCertificates(Identity identity, Partner? partner = null) =>
        new[] { partner?.OwnCertificate, identity.DecryptionCertificate, identity.PreviousDecryptionCertificate, identity.SigningCertificate }
            .OfType<Certificate>()
            .Where(c => c.HasPrivateKey)
            .DistinctBy(c => c.Id)
            .Select(CertificateLoader.Load)
            .ToList();

    /// <summary>The certificate our messages and MDNs to the partner are signed with: its own one, or the identity's.</summary>
    public static X509Certificate2? SigningCertificate(Identity identity, Partner? partner = null) =>
        SigningCertificateEntity(identity, partner) is { HasPrivateKey: true } certificate ? CertificateLoader.Load(certificate) : null;

    /// <summary>The stored certificate <see cref="SigningCertificate"/> loads.</summary>
    public static Certificate? SigningCertificateEntity(Identity identity, Partner? partner) =>
        partner?.OwnCertificate ?? identity.SigningCertificate;
}
