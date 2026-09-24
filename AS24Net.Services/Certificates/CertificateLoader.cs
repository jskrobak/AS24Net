using System.Security.Cryptography.X509Certificates;
using AS24Net.Domain;

namespace AS24Net.Services.Certificates;

public static class CertificateLoader
{
    /// <summary>Creates an <see cref="X509Certificate2"/> from a stored certificate (PKCS#12 with key, or DER/PEM).</summary>
    public static X509Certificate2 Load(Certificate certificate)
    {
        if (string.IsNullOrEmpty(certificate.Base64Data))
            throw new InvalidOperationException($"Certificate '{certificate.Name}' has no data.");

        var data = Convert.FromBase64String(certificate.Base64Data);

        return certificate.HasPrivateKey
            ? X509CertificateLoader.LoadPkcs12(data, certificate.Password)
            : X509CertificateLoader.LoadCertificate(data);
    }

    /// <summary>
    /// Creates the database record of a certificate file: PKCS#12 (.pfx/.p12, with private key) or DER/PEM
    /// (.cer, .crt, .pem).
    /// </summary>
    public static Certificate CreateEntity(byte[] data, string fileName, string? password)
    {
        var isPkcs12 = Path.GetExtension(fileName).ToLowerInvariant() is ".pfx" or ".p12";
        using var cert = isPkcs12
            ? X509CertificateLoader.LoadPkcs12(data, password)
            : X509CertificateLoader.LoadCertificate(data);

        return new Certificate
        {
            Name = cert.Subject,
            HasPrivateKey = cert.HasPrivateKey,
            ValidFrom = cert.NotBefore,
            ValidTo = cert.NotAfter,
            Thumbprint = cert.Thumbprint,
            // A certificate without key is stored as DER, whichever way it came.
            Base64Data = Convert.ToBase64String(cert.HasPrivateKey ? data : cert.RawData),
            Password = cert.HasPrivateKey ? password ?? string.Empty : string.Empty,
        };
    }
}
