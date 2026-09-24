using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AS24Net.Core.Tests;

/// <summary>Self-signed certificates of two stations, created once for all tests.</summary>
internal static class TestCertificates
{
    public static readonly X509Certificate2 Alice = Create("alice");
    public static readonly X509Certificate2 Bob = Create("bob");
    public static readonly X509Certificate2 Mallory = Create("mallory");

    /// <summary>The public part only, as a partner knows it.</summary>
    public static X509Certificate2 Public(X509Certificate2 certificate) => X509CertificateLoader.LoadCertificate(certificate.RawData);

    private static X509Certificate2 Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // Exported and loaded again, so that the key can be used by the CMS classes on every platform.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "test"), "test");
    }
}
