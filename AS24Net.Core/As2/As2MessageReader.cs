using System.Security.Cryptography.X509Certificates;
using AS24Net.Core.Mime;
using AS24Net.Core.Security;

namespace AS24Net.Core.As2;

/// <summary>A received message after decryption, signature verification and decompression.</summary>
public sealed class As2InboundMessage
{
    public required byte[] Payload { get; init; }

    /// <summary>Media type of the payload as the partner sent it.</summary>
    public required string ContentType { get; init; }

    /// <summary>File name from <c>Content-Disposition</c>, if the partner sent one.</summary>
    public string? FileName { get; init; }

    public bool Signed { get; init; }
    public bool Encrypted { get; init; }
    public bool Compressed { get; init; }

    /// <summary>The partner's certificate the signature was verified with.</summary>
    public X509Certificate2? SignatureCertificate { get; init; }

    public string? SignatureAlgorithm { get; init; }
    public string? EncryptionAlgorithm { get; init; }

    /// <summary>The MIC to return in the MDN (<c>Received-Content-MIC</c>).</summary>
    public required string Mic { get; init; }
}

/// <summary>
/// Opens a received AS2 message (RFC 4130): decrypts it, verifies its signature and decompresses it, whatever the
/// order the partner applied them in.
/// </summary>
public static class As2MessageReader
{
    /// <summary>Layers of S/MIME a message may be wrapped in; more are refused.</summary>
    private const int MaxLayers = 6;

    /// <param name="headers">HTTP headers of the request; Content-Type describes the body.</param>
    /// <param name="body">HTTP body.</param>
    /// <param name="decryptionCertificates">Our certificates with private key the message may be encrypted for.</param>
    /// <param name="signatureCertificates">The partner's certificates its signature may be made with.</param>
    /// <param name="micAlgorithm">Digest algorithm of the MIC, for an unsigned message and when the MDN request asks for none.</param>
    /// <exception cref="As2ProcessingException">The message cannot be opened; the error is returned in the MDN.</exception>
    public static As2InboundMessage Read(IReadOnlyDictionary<string, string> headers, byte[] body,
        IReadOnlyCollection<X509Certificate2> decryptionCertificates, IReadOnlyCollection<X509Certificate2> signatureCertificates,
        string micAlgorithm = As2Algorithms.Sha256)
    {
        var mdnRequest = MdnRequest.FromHeaders(headers);

        // The entity described by the HTTP headers.
        var entity = MimeEntity.FromHeaders(
            headers.Where(h => h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)), body);
        var outerFileName = entity.FileName;

        bool signed = false, encrypted = false, compressed = false;
        X509Certificate2? signer = null;
        string? signatureAlgorithm = null, encryptionAlgorithm = null;
        string? mic = null;

        try
        {
            for (var layer = 0; ; layer++)
            {
                if (layer >= MaxLayers)
                    throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The message is wrapped in too many layers.");

                var contentType = entity.ContentType;
                if (contentType.IsPkcs7Mime && IsCompressed(contentType))
                {
                    var data = CmsCompression.Decompress(Der(entity));
                    entity = MimeEntity.Parse(data);
                    compressed = true;
                    continue;
                }

                if (contentType.IsPkcs7Mime)
                {
                    if (encrypted)
                        throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The message is encrypted twice.");

                    var smimeType = contentType["smime-type"];
                    if (smimeType is not null && !smimeType.Equals("enveloped-data", StringComparison.OrdinalIgnoreCase))
                        throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, $"S/MIME type '{smimeType}' is not supported.");

                    var (data, algorithm) = Smime.Decrypt(Der(entity), decryptionCertificates);
                    encrypted = true;
                    encryptionAlgorithm = algorithm;
                    // Unless a signature follows, the MIC is computed over the decrypted MIME entity.
                    if (!signed)
                        mic = Mic.Compute(data, mdnRequest?.MicAlgorithm(micAlgorithm) ?? micAlgorithm);

                    if (TryParseEntity(data) is { } inner)
                    {
                        entity = inner;
                        continue;
                    }

                    // Not a MIME entity: the payload itself. Mendelson AS2 encrypts an unsigned, uncompressed message
                    // that way, without the MIME entity S/MIME puts into the envelope; its name and type are unknown.
                    return new As2InboundMessage
                    {
                        Payload = data,
                        ContentType = "application/octet-stream",
                        FileName = outerFileName is { } name && !name.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase) ? name : null,
                        Signed = signed,
                        Encrypted = true,
                        Compressed = compressed,
                        SignatureCertificate = signer,
                        SignatureAlgorithm = signatureAlgorithm,
                        EncryptionAlgorithm = encryptionAlgorithm,
                        Mic = mic!,
                    };
                }

                if (contentType.Is("multipart/signed"))
                {
                    if (signed)
                        throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The message is signed twice.");

                    var parts = entity.GetParts();
                    if (parts.Count != 2 || !parts[1].ContentType.IsPkcs7Signature)
                        throw new As2ProcessingException(As2Errors.IntegrityCheckFailed,
                            "A signed message has to consist of the content and an application/pkcs7-signature part.");

                    var content = parts[0].ToBytes();
                    var signature = parts[1].DecodeBody();
                    var verification = VerifyWithCanonicalFallback(content, signature, signatureCertificates);
                    signed = true;
                    signer = verification.Certificate;
                    signatureAlgorithm = verification.DigestAlgorithm ?? As2Algorithms.NormalizeDigest(contentType["micalg"]);
                    // The MIC of a signed message is computed with the digest of its signature. That is what AS2 software
                    // does in practice when it compares the MIC, and it is one of the algorithms the sender asked for.
                    mic = Mic.Compute(content, signatureAlgorithm ?? mdnRequest?.MicAlgorithm(micAlgorithm) ?? micAlgorithm);
                    entity = parts[0];
                    continue;
                }

                var payload = entity.DecodeBody();
                // A message that is neither signed nor encrypted: the MIC is computed over its content alone.
                mic ??= Mic.Compute(body, mdnRequest?.MicAlgorithm(micAlgorithm) ?? micAlgorithm);

                return new As2InboundMessage
                {
                    Payload = payload,
                    ContentType = entity.ContentType.MediaType,
                    FileName = entity.FileName,
                    Signed = signed,
                    Encrypted = encrypted,
                    Compressed = compressed,
                    SignatureCertificate = signer,
                    SignatureAlgorithm = signatureAlgorithm,
                    EncryptionAlgorithm = encryptionAlgorithm,
                    Mic = mic,
                };
            }
        }
        catch (MimeFormatException ex)
        {
            throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The message is not valid MIME: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// The decrypted content as a MIME entity; <c>null</c> when it is none. Every entity S/MIME encrypts has a
    /// Content-Type, while a bare payload fails to parse or has none (EDIFACT may start with a line such as
    /// <c>UNA:+.? '</c>, which alone looks like a header).
    /// </summary>
    private static MimeEntity? TryParseEntity(byte[] data)
    {
        try
        {
            var entity = MimeEntity.Parse(data);
            return entity["Content-Type"] is null ? null : entity;
        }
        catch (MimeFormatException)
        {
            return null;
        }
    }

    private static bool IsCompressed(ContentType contentType) =>
        string.Equals(contentType["smime-type"], "compressed-data", StringComparison.OrdinalIgnoreCase);

    /// <summary>The DER of an S/MIME body; base64 when the transfer encoding says so or when it is not DER.</summary>
    private static byte[] Der(MimeEntity entity)
    {
        if (entity.TransferEncoding == "base64")
            return entity.DecodeBody();

        // DER starts with a SEQUENCE; some partners send base64 without saying so.
        if (entity.Body.Length > 0 && entity.Body[0] == 0x30)
            return entity.Body;

        try
        {
            return MimeEntity.DecodeBase64(entity.Body);
        }
        catch (MimeFormatException)
        {
            return entity.Body;
        }
    }

    /// <summary>
    /// Verifies the signature over the content as it came; when that fails and the content has bare line feeds,
    /// once more over the content with CRLF line ends, which RFC 5751 makes the canonical form of text that a
    /// gateway on the way may have changed.
    /// </summary>
    private static SignatureVerification VerifyWithCanonicalFallback(byte[] content, byte[] signature,
        IReadOnlyCollection<X509Certificate2> certificates)
    {
        try
        {
            return Smime.Verify(content, signature, certificates);
        }
        catch (As2ProcessingException) when (HasBareLineFeed(content))
        {
            return Smime.Verify(Canonicalize(content), signature, certificates);
        }
    }

    private static bool HasBareLineFeed(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
            if (data[i] == '\n' && (i == 0 || data[i - 1] != '\r'))
                return true;
        return false;
    }

    internal static byte[] Canonicalize(byte[] data)
    {
        using var result = new MemoryStream(data.Length + data.Length / 40);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n' && (i == 0 || data[i - 1] != '\r'))
                result.WriteByte((byte)'\r');
            result.WriteByte(data[i]);
        }

        return result.ToArray();
    }
}
