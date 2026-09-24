using System.Security.Cryptography.X509Certificates;
using System.Text;
using AS24Net.Core.Mime;
using AS24Net.Core.Security;

namespace AS24Net.Core.As2;

/// <summary>
/// A Message Disposition Notification (RFC 3798, RFC 4130 7): the receipt of an AS2 message. It is positive when
/// the disposition is <c>processed</c> without an error or failure modifier.
/// </summary>
public sealed class Mdn
{
    public const string ProcessedDisposition = "automatic-action/MDN-sent-automatically; processed";

    public string? MessageId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }

    public string? ReportingUa { get; init; }
    public string? OriginalRecipient { get; init; }
    public string? FinalRecipient { get; init; }

    /// <summary>Message-ID of the message the MDN is about.</summary>
    public string? OriginalMessageId { get; init; }

    /// <summary>The whole disposition, e.g. <c>automatic-action/MDN-sent-automatically; processed/error: decryption-failed</c>.</summary>
    public required string Disposition { get; init; }

    /// <summary>The MIC the receiver computed (<c>Received-Content-MIC</c>).</summary>
    public string? ReceivedContentMic { get; init; }

    /// <summary>The human readable part.</summary>
    public string? Text { get; init; }

    public bool Signed { get; init; }

    /// <summary>The partner's certificate the signature of the MDN was verified with.</summary>
    public X509Certificate2? SignatureCertificate { get; init; }

    /// <summary>
    /// The modifier after "processed/" or the disposition type that is not "processed", e.g. <c>error: decryption-failed</c>;
    /// <c>null</c> for a positive MDN.
    /// </summary>
    public string? Problem
    {
        get
        {
            var semicolon = Disposition.IndexOf(';');
            var type = (semicolon < 0 ? Disposition : Disposition[(semicolon + 1)..]).Trim();
            var slash = type.IndexOf('/');
            var dispositionType = (slash < 0 ? type : type[..slash]).Trim();
            if (!dispositionType.Equals("processed", StringComparison.OrdinalIgnoreCase))
                return type;

            if (slash < 0)
                return null;

            // A warning (e.g. "warning: duplicate-document") still means processed.
            var modifier = type[(slash + 1)..].Trim();
            return modifier.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ? null : modifier;
        }
    }

    public bool IsPositive => Problem is null;

    /// <summary>The disposition of a message that failed with the error, e.g. <c>…; processed/error: decryption-failed</c>.</summary>
    public static string ErrorDisposition(string error) => $"{ProcessedDisposition}/error: {error}";

    /// <summary>The disposition of a message processed with a warning (e.g. a duplicate).</summary>
    public static string WarningDisposition(string warning) => $"{ProcessedDisposition}/warning: {warning}";
}

/// <summary>What an MDN to be sent says.</summary>
public sealed class MdnOptions
{
    /// <summary>Our AS2 name: the MDN comes from the recipient of the message.</summary>
    public required string From { get; init; }

    /// <summary>The partner's AS2 name.</summary>
    public required string To { get; init; }

    public required string OriginalMessageId { get; init; }
    public required string Disposition { get; init; }
    public string? ReceivedContentMic { get; init; }
    public string? Text { get; init; }

    /// <summary>Our certificate with private key; the MDN is signed when set.</summary>
    public X509Certificate2? SigningCertificate { get; init; }

    public string SignatureAlgorithm { get; init; } = As2Algorithms.Sha256;
    public string? Host { get; init; }
    public string? Subject { get; init; }
}

/// <summary>An MDN ready to be sent: in the HTTP response of the message, or posted to the partner.</summary>
public sealed class As2OutboundMdn
{
    public required string MessageId { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required byte[] Body { get; init; }
}

/// <summary>Builds and reads MDNs (multipart/report; report-type=disposition-notification), signed or not.</summary>
public static class MdnProcessor
{
    public static As2OutboundMdn Build(MdnOptions options)
    {
        var text = options.Text ?? DefaultText(options);
        var textPart = MimeEntity.Create("text/plain; charset=us-ascii", Encoding.ASCII.GetBytes(As2Headers.Sanitize(text) + "\r\n"),
            ("Content-Transfer-Encoding", "7bit"));

        var fields = new StringBuilder();
        fields.Append($"Reporting-UA: {As2Headers.Product}\r\n");
        fields.Append($"Original-Recipient: rfc822; {As2Headers.QuoteAs2Id(options.From)}\r\n");
        fields.Append($"Final-Recipient: rfc822; {As2Headers.QuoteAs2Id(options.From)}\r\n");
        fields.Append($"Original-Message-ID: {As2Headers.NormalizeMessageId(options.OriginalMessageId)}\r\n");
        if (!string.IsNullOrEmpty(options.ReceivedContentMic))
            fields.Append($"Received-Content-MIC: {options.ReceivedContentMic}\r\n");
        fields.Append($"Disposition: {options.Disposition}\r\n");
        var notificationPart = MimeEntity.Create("message/disposition-notification", Encoding.ASCII.GetBytes(fields.ToString()),
            ("Content-Transfer-Encoding", "7bit"));

        var reportType = new ContentType("multipart/report");
        reportType.Parameters["report-type"] = "disposition-notification";
        var entity = MimeEntity.CreateMultipart(reportType, [textPart.ToBytes(), notificationPart.ToBytes()]);

        if (options.SigningCertificate is { } certificate)
        {
            var content = entity.ToBytes();
            var signature = Smime.Sign(content, certificate, options.SignatureAlgorithm);
            var signaturePart = MimeEntity.Create("application/pkcs7-signature; name=smime.p7s", MimeEntity.EncodeBase64(signature),
                ("Content-Transfer-Encoding", "base64"),
                ("Content-Disposition", "attachment; filename=smime.p7s"));

            var signedType = new ContentType("multipart/signed");
            signedType.Parameters["protocol"] = "application/pkcs7-signature";
            signedType.Parameters["micalg"] = As2Algorithms.NormalizeDigest(options.SignatureAlgorithm)!;
            entity = MimeEntity.CreateMultipart(signedType, [content, signaturePart.ToBytes()]);
        }

        var messageId = As2Headers.NewMessageId(options.Host);
        var headers = As2Headers.NewCollection();
        headers[As2Headers.As2Version] = As2Headers.Version;
        headers[As2Headers.EdiintFeatures] = As2Headers.Features;
        headers[As2Headers.MessageId] = messageId;
        headers[As2Headers.As2From] = As2Headers.QuoteAs2Id(options.From);
        headers[As2Headers.As2To] = As2Headers.QuoteAs2Id(options.To);
        headers[As2Headers.Subject] = As2Headers.Sanitize(options.Subject ?? "Message Disposition Notification");
        headers[As2Headers.Date] = As2Headers.Now();
        headers[As2Headers.MimeVersion] = "1.0";
        foreach (var header in entity.Headers)
            headers[header.Name] = header.Value;

        return new As2OutboundMdn { MessageId = messageId, Headers = headers, Body = entity.Body };
    }

    /// <summary>Is the body with these headers an MDN (as opposed to a message)?</summary>
    public static bool IsMdn(IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        headers.TryGetValue(As2Headers.ContentType, out var value);
        var contentType = ContentType.Parse(value);
        if (contentType.Is("multipart/report"))
            return true;

        if (!contentType.Is("multipart/signed"))
            return false;

        // A signed MDN: the signed content is the report.
        try
        {
            var parts = MimeEntity.FromHeaders(headers.Where(h => h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)), body).GetParts();
            return parts.Count > 0 && parts[0].ContentType.Is("multipart/report");
        }
        catch (MimeFormatException)
        {
            return false;
        }
    }

    /// <summary>Reads an MDN and verifies its signature with the partner's certificates.</summary>
    /// <param name="requireSignature">An unsigned MDN is refused (we asked for a signed one).</param>
    /// <exception cref="As2ProcessingException">The MDN cannot be read or its signature is not valid.</exception>
    public static Mdn Read(IReadOnlyDictionary<string, string> headers, byte[] body,
        IReadOnlyCollection<X509Certificate2> signatureCertificates, bool requireSignature)
    {
        try
        {
            var entity = MimeEntity.FromHeaders(headers.Where(h => h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)), body);
            var signed = false;
            X509Certificate2? signer = null;

            if (entity.ContentType.Is("multipart/signed"))
            {
                var parts = entity.GetParts();
                if (parts.Count != 2 || !parts[1].ContentType.IsPkcs7Signature)
                    throw new As2ProcessingException(As2Errors.IntegrityCheckFailed, "The signed MDN has no signature part.");

                signer = Smime.Verify(parts[0].ToBytes(), parts[1].DecodeBody(), signatureCertificates).Certificate;
                signed = true;
                entity = parts[0];
            }
            else if (requireSignature)
            {
                throw new As2ProcessingException(As2Errors.InsufficientMessageSecurity, "A signed MDN was requested, but the MDN is not signed.");
            }

            if (!entity.ContentType.Is("multipart/report"))
                throw new As2ProcessingException(As2Errors.UnexpectedProcessingError,
                    $"The MDN is of type {entity.ContentType.MediaType}, not multipart/report.");

            string? text = null;
            Dictionary<string, string>? fields = null;
            foreach (var part in entity.GetParts())
            {
                if (part.ContentType.Is("message/disposition-notification"))
                    fields = ParseFields(part.DecodeBody());
                else if (part.ContentType.Is("text/plain") && text is null)
                    text = Encoding.UTF8.GetString(part.DecodeBody()).Trim();
            }

            if (fields is null || !fields.TryGetValue("Disposition", out var disposition))
                throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The MDN has no disposition.");

            headers.TryGetValue(As2Headers.MessageId, out var messageId);
            headers.TryGetValue(As2Headers.As2From, out var from);
            headers.TryGetValue(As2Headers.As2To, out var to);
            return new Mdn
            {
                MessageId = messageId,
                From = from is null ? null : As2Headers.UnquoteAs2Id(from),
                To = to is null ? null : As2Headers.UnquoteAs2Id(to),
                ReportingUa = fields.GetValueOrDefault("Reporting-UA"),
                OriginalRecipient = fields.GetValueOrDefault("Original-Recipient"),
                FinalRecipient = fields.GetValueOrDefault("Final-Recipient"),
                OriginalMessageId = fields.TryGetValue("Original-Message-ID", out var original) ? As2Headers.NormalizeMessageId(original) : null,
                Disposition = disposition,
                ReceivedContentMic = fields.GetValueOrDefault("Received-Content-MIC"),
                Text = text,
                Signed = signed,
                SignatureCertificate = signer,
            };
        }
        catch (MimeFormatException ex)
        {
            throw new As2ProcessingException(As2Errors.UnexpectedProcessingError, "The MDN is not valid MIME: " + ex.Message, ex);
        }
    }

    /// <summary>The fields of a disposition notification: header lines, possibly folded.</summary>
    private static Dictionary<string, string> ParseFields(byte[] data)
    {
        var entity = MimeEntity.Parse([.. data, (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n']);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in entity.Headers)
            fields.TryAdd(header.Name, header.Value);
        return fields;
    }

    private static string DefaultText(MdnOptions options) =>
        options.Disposition.Contains("error", StringComparison.OrdinalIgnoreCase) || options.Disposition.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? $"The AS2 message {options.OriginalMessageId} sent to {options.From} could not be processed: {options.Disposition}."
            : $"The AS2 message {options.OriginalMessageId} sent to {options.From} was received. This is no guarantee that the content was processed by the application.";
}
