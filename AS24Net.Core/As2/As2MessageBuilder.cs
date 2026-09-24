using System.Security.Cryptography.X509Certificates;
using AS24Net.Core.Mime;
using AS24Net.Core.Security;

namespace AS24Net.Core.As2;

/// <summary>What a message to a partner consists of and how it is to be secured.</summary>
public sealed class As2OutboundOptions
{
    public required string From { get; init; }
    public required string To { get; init; }

    /// <summary>Message-ID; a new one is created when empty.</summary>
    public string? MessageId { get; init; }

    public string Subject { get; init; } = "AS2 message";
    public required byte[] Payload { get; init; }

    /// <summary>Media type of the payload, e.g. <c>application/edifact</c>, <c>application/xml</c>.</summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>File name sent in <c>Content-Disposition</c>; the partner may store the file under it.</summary>
    public string? FileName { get; init; }

    public bool Compress { get; init; }

    /// <summary>Compress the payload before signing it (RFC 5402 recommends it), otherwise the signed message.</summary>
    public bool CompressBeforeSigning { get; init; } = true;

    /// <summary>Our certificate with private key; the message is signed when set.</summary>
    public X509Certificate2? SigningCertificate { get; init; }

    public string SignatureAlgorithm { get; init; } = As2Algorithms.Sha256;

    /// <summary>The partner's certificate; the message is encrypted for it when set.</summary>
    public X509Certificate2? EncryptionCertificate { get; init; }

    public string EncryptionAlgorithm { get; init; } = As2Algorithms.Aes256;

    /// <summary>Asks for an MDN; <c>null</c> sends the message without asking for one.</summary>
    public MdnRequest? Mdn { get; init; }

    /// <summary>Host name for the Message-ID.</summary>
    public string? Host { get; init; }
}

/// <summary>A message ready to be posted: the HTTP headers and body, and the MIC the MDN is expected to return.</summary>
public sealed class As2OutboundMessage
{
    public required string MessageId { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required byte[] Body { get; init; }

    /// <summary>The MIC computed over the content the partner is to compute it over, in the MDN's algorithm.</summary>
    public required string Mic { get; init; }

    public bool Signed { get; init; }
    public bool Encrypted { get; init; }
    public bool Compressed { get; init; }
}

/// <summary>
/// Builds AS2 messages (RFC 4130): the payload as a MIME entity, optionally compressed (RFC 5402), signed
/// (multipart/signed) and encrypted (application/pkcs7-mime enveloped-data), in this order.
/// </summary>
public static class As2MessageBuilder
{
    public static As2OutboundMessage Build(As2OutboundOptions options)
    {
        var messageId = As2Headers.NormalizeMessageId(string.IsNullOrWhiteSpace(options.MessageId)
            ? As2Headers.NewMessageId(options.Host)
            : options.MessageId);
        // A signed message: the MIC is computed with the digest of the signature, as the receiver does it (see
        // As2MessageReader); otherwise with the first algorithm a signed MDN is asked for (only a request for a signed
        // MDN names algorithms), or SHA-256.
        var micAlgorithm = options.SigningCertificate is not null
            ? As2Algorithms.NormalizeDigest(options.SignatureAlgorithm) ?? As2Algorithms.Sha256
            : options.Mdn is { Signed: true } mdnRequest ? mdnRequest.MicAlgorithm(As2Algorithms.Sha256) : As2Algorithms.Sha256;

        var headers = new List<(string, string)> { ("Content-Transfer-Encoding", "binary") };
        if (!string.IsNullOrWhiteSpace(options.FileName))
            headers.Add(("Content-Disposition", "attachment; filename=" + ContentType.Quote(As2Headers.Sanitize(options.FileName.Trim()))));
        var entity = MimeEntity.Create(options.ContentType, options.Payload, headers.ToArray());

        // Without a signature there is nothing to compress after: the payload is compressed right away.
        var signed = options.SigningCertificate is not null;
        var compressed = false;
        if (options.Compress && (options.CompressBeforeSigning || !signed))
        {
            entity = CompressEntity(entity);
            compressed = true;
        }

        // The MIC is computed over what the signature covers; for an unsigned message over the MIME entity that is
        // encrypted, or over the content alone when the message is not secured at all (RFC 4130 7.3.1).
        byte[] micContent;
        if (signed)
        {
            var signedContent = entity.ToBytes();
            micContent = signedContent;
            var signature = Smime.Sign(signedContent, options.SigningCertificate!, options.SignatureAlgorithm);
            var signaturePart = MimeEntity.Create("application/pkcs7-signature; name=smime.p7s", MimeEntity.EncodeBase64(signature),
                ("Content-Transfer-Encoding", "base64"),
                ("Content-Disposition", "attachment; filename=smime.p7s"));

            var contentType = new ContentType("multipart/signed");
            contentType.Parameters["protocol"] = "application/pkcs7-signature";
            contentType.Parameters["micalg"] = As2Algorithms.NormalizeDigest(options.SignatureAlgorithm)!;
            entity = MimeEntity.CreateMultipart(contentType, [signedContent, signaturePart.ToBytes()]);
        }
        else
        {
            micContent = options.EncryptionCertificate is not null ? entity.ToBytes() : entity.Body;
        }

        if (options.Compress && !compressed)
        {
            entity = CompressEntity(entity);
            compressed = true;
        }

        var encrypted = options.EncryptionCertificate is not null;
        if (encrypted)
        {
            var enveloped = Smime.Encrypt(entity.ToBytes(), options.EncryptionCertificate!, options.EncryptionAlgorithm);
            entity = MimeEntity.Create("application/pkcs7-mime; smime-type=enveloped-data; name=smime.p7m", enveloped,
                ("Content-Transfer-Encoding", "binary"),
                ("Content-Disposition", "attachment; filename=smime.p7m"));
        }

        var http = As2Headers.NewCollection();
        http[As2Headers.As2Version] = As2Headers.Version;
        http[As2Headers.EdiintFeatures] = As2Headers.Features;
        http[As2Headers.MessageId] = messageId;
        http[As2Headers.As2From] = As2Headers.QuoteAs2Id(options.From);
        http[As2Headers.As2To] = As2Headers.QuoteAs2Id(options.To);
        http[As2Headers.Subject] = As2Headers.Sanitize(options.Subject);
        http[As2Headers.Date] = As2Headers.Now();
        http[As2Headers.MimeVersion] = "1.0";
        foreach (var header in entity.Headers)
            http[header.Name] = header.Value;

        if (options.Mdn is { } mdn)
        {
            http[As2Headers.DispositionNotificationTo] = string.IsNullOrWhiteSpace(mdn.NotificationTo) ? options.From : mdn.NotificationTo;
            if (mdn.Signed)
                http[As2Headers.DispositionNotificationOptions] = MdnRequest.FormatOptions(micAlgorithm);
            if (mdn.IsAsync)
                http[As2Headers.ReceiptDeliveryOption] = mdn.ReturnUrl!;
        }

        return new As2OutboundMessage
        {
            MessageId = messageId,
            Headers = http,
            Body = entity.Body,
            Mic = Mic.Compute(micContent, micAlgorithm),
            Signed = signed,
            Encrypted = encrypted,
            Compressed = compressed,
        };
    }

    private static MimeEntity CompressEntity(MimeEntity entity) =>
        MimeEntity.Create("application/pkcs7-mime; smime-type=compressed-data; name=smime.p7z",
            CmsCompression.Compress(entity.ToBytes()),
            ("Content-Transfer-Encoding", "binary"),
            ("Content-Disposition", "attachment; filename=smime.p7z"));
}
