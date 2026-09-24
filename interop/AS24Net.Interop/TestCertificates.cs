using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AS24Net.Interop;

/// <summary>
/// Self-signed key pairs of the stations taking part in the tests, created once in <c>work/certs</c> and mounted
/// into the containers: <c>name.pfx</c> (with the private key, password <see cref="Password"/>), <c>name.pem</c>
/// (the certificate) and <c>name.key</c> (the private key, unencrypted PEM, for tools that want it apart).
/// </summary>
public static class TestCertificates
{
    public const string Password = "interop";

    public static readonly string[] Stations = ["as24net", "pyas2", "openas2", "mendelson", "load"];

    public static void EnsureCreated(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in Stations)
        {
            if (File.Exists(Path.Combine(directory, name + ".pfx")))
                continue;

            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}.interop.as24net, O=AS24Net interop tests", key,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

            // PKCS#12 with AES and SHA-256, which Java, Python and OpenSSL 3 all read.
            File.WriteAllBytes(Path.Combine(directory, name + ".pfx"), certificate.ExportPkcs12(
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048), Password));
            File.WriteAllText(Path.Combine(directory, name + ".pem"), certificate.ExportCertificatePem() + "\n");
            File.WriteAllText(Path.Combine(directory, name + ".key"), key.ExportPkcs8PrivateKeyPem() + "\n");
            Console.WriteLine($"Created the test certificate {name}.");
        }
    }

    public static X509Certificate2 Load(string directory, string name) =>
        X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory, name + ".pfx"), Password, X509KeyStorageFlags.Exportable);

    public static X509Certificate2 LoadPublic(string directory, string name) =>
        X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(directory, name + ".pem")));
}
