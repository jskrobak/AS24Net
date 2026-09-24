using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using AS24Net.Core.As2;

namespace AS24Net.Core.Security;

/// <summary>The certificate a signature was verified with and the digest algorithm of the signature.</summary>
public sealed record SignatureVerification(X509Certificate2 Certificate, string? DigestAlgorithm);

/// <summary>S/MIME signatures (detached CMS SignedData, RFC 5751) and encryption (CMS EnvelopedData).</summary>
public static class Smime
{
    /// <summary>A detached signature of the content (DER), with the signing time and the signer's certificate.</summary>
    public static byte[] Sign(byte[] content, X509Certificate2 certificate, string digestAlgorithm)
    {
        if (!certificate.HasPrivateKey)
            throw new ArgumentException($"The certificate {certificate.Subject} has no private key to sign with.", nameof(certificate));

        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = As2Algorithms.DigestOid(digestAlgorithm),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }

    /// <summary>
    /// Verifies a detached signature of the content with one of the certificates the partner is known by (the
    /// current one and the one before a roll-over). The certificate embedded in the signature is not trusted by
    /// itself: its public key has to be the one of a known certificate.
    /// </summary>
    /// <exception cref="As2ProcessingException">The signature is invalid or made with an unknown certificate.</exception>
    public static SignatureVerification Verify(byte[] content, byte[] signature, IReadOnlyCollection<X509Certificate2> certificates)
    {
        if (certificates.Count == 0)
            throw new As2ProcessingException(As2Errors.AuthenticationFailed,
                "The message is signed, but no certificate of the partner is configured to verify it.");

        SignedCms cms;
        try
        {
            cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(signature);
        }
        catch (CryptographicException ex)
        {
            throw new As2ProcessingException(As2Errors.IntegrityCheckFailed, "The signature cannot be read: " + ex.Message, ex);
        }

        if (cms.SignerInfos.Count == 0)
            throw new As2ProcessingException(As2Errors.IntegrityCheckFailed, "The signature contains no signer.");

        string? lastError = null;
        foreach (var signerInfo in cms.SignerInfos)
        {
            foreach (var certificate in certificates)
            {
                // A certificate sent with the signature is used only when it carries a known key.
                if (signerInfo.Certificate is { } embedded && !SameKey(embedded, certificate))
                    continue;

                try
                {
                    signerInfo.CheckSignature(new X509Certificate2Collection(certificate), verifySignatureOnly: true);
                    return new SignatureVerification(certificate, As2Algorithms.DigestName(signerInfo.DigestAlgorithm));
                }
                catch (CryptographicException ex)
                {
                    lastError = ex.Message;
                }
            }
        }

        throw lastError is null
            ? new As2ProcessingException(As2Errors.AuthenticationFailed,
                "The message is signed with a certificate that is not configured for the partner.")
            : new As2ProcessingException(As2Errors.IntegrityCheckFailed,
                "The signature does not match the content: " + lastError);
    }

    /// <summary>Encrypts the content for the recipient's certificate (CMS EnvelopedData, DER).</summary>
    public static byte[] Encrypt(byte[] content, X509Certificate2 recipient, string algorithm)
    {
        var cms = new EnvelopedCms(new ContentInfo(content), new AlgorithmIdentifier(As2Algorithms.EncryptionOid(algorithm)));
        cms.Encrypt(new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, recipient));
        return cms.Encode();
    }

    /// <summary>Decrypts EnvelopedData with whichever of our certificates it was encrypted for.</summary>
    /// <returns>The content and the name of the content encryption algorithm.</returns>
    public static (byte[] Content, string? Algorithm) Decrypt(byte[] envelopedData, IReadOnlyCollection<X509Certificate2> certificates)
    {
        var cms = new EnvelopedCms();
        try
        {
            cms.Decode(envelopedData);
        }
        catch (CryptographicException ex)
        {
            throw new As2ProcessingException(As2Errors.DecryptionFailed, "The encrypted content cannot be read: " + ex.Message, ex);
        }

        var keys = certificates.Where(c => c.HasPrivateKey).ToList();
        if (keys.Count == 0)
            throw new As2ProcessingException(As2Errors.DecryptionFailed,
                "The message is encrypted, but no certificate with a private key is configured to decrypt it.");

        try
        {
            cms.Decrypt(new X509Certificate2Collection(keys.ToArray()));
        }
        catch (CryptographicException ex)
        {
            throw new As2ProcessingException(As2Errors.DecryptionFailed,
                "The message cannot be decrypted with our certificates (it may be encrypted for another one): " + ex.Message, ex);
        }

        return (cms.ContentInfo.Content, As2Algorithms.EncryptionName(cms.ContentEncryptionAlgorithm.Oid));
    }

    private static bool SameKey(X509Certificate2 a, X509Certificate2 b) =>
        a.PublicKey.EncodedKeyValue.RawData.AsSpan().SequenceEqual(b.PublicKey.EncodedKeyValue.RawData);
}
