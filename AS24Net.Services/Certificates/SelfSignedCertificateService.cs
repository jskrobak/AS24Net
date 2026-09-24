using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Havit.Data.Patterns.UnitOfWorks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;

namespace AS24Net.Services.Certificates;

public sealed class SelfSignedCertificateOptions
{
    /// <summary>Create a certificate on startup when the database contains none with a private key.</summary>
    public bool GenerateCertificate { get; set; } = true;

    /// <summary>Subject (CN) of the certificate. Defaults to the machine (container) name.</summary>
    public string? CertificateSubject { get; set; }

    public int CertificateValidityDays { get; set; } = 3 * 365;
}

/// <summary>
/// Creates a self-signed certificate for signing and decryption on first start, so that every installation has
/// its own key and messages can be secured right away. Replace it by one of a certification authority where the
/// partners want that.
/// </summary>
public class SelfSignedCertificateService(
    ICertificateRepository certificateRepository,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<SelfSignedCertificateService> logger)
{
    public async Task<Certificate?> EnsureCertificateAsync(CancellationToken cancellationToken = default)
    {
        var options = configuration.GetSection("Certificates").Get<SelfSignedCertificateOptions>() ?? new SelfSignedCertificateOptions();
        if (!options.GenerateCertificate)
            return null;

        var certificates = await certificateRepository.GetAllAsync(cancellationToken);
        if (certificates.Any(c => c.HasPrivateKey))
            return null;

        var subject = string.IsNullOrWhiteSpace(options.CertificateSubject)
            ? Environment.MachineName.ToLowerInvariant()
            : options.CertificateSubject.Trim();

        var entity = CreateEntity(subject, TimeSpan.FromDays(options.CertificateValidityDays));
        unitOfWork.AddForInsert(entity);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogWarning(
            "Created the self-signed AS2 certificate {Subject} (SHA-1 {Thumbprint}), valid to {ValidTo:d}. Select it for your identities " +
            "and give its public part (Certificates / download) to your partners.",
            entity.Name, entity.Thumbprint, entity.ValidTo);
        return entity;
    }

    /// <summary>A new self-signed certificate for signing and encryption, as it is stored.</summary>
    public static Certificate CreateEntity(string subject, TimeSpan validity)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        using var certificate = Create(subject, validity);
        return new Certificate
        {
            Name = certificate.Subject,
            HasPrivateKey = true,
            ValidFrom = certificate.NotBefore,
            ValidTo = certificate.NotAfter,
            Thumbprint = certificate.Thumbprint,
            Base64Data = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password)),
            Password = password,
        };
    }

    /// <summary>Creates a self-signed certificate for S/MIME signatures and encryption.</summary>
    public static X509Certificate2 Create(string subject, TimeSpan validity)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={subject}, O=AS24Net"), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.Add(validity));
    }
}
