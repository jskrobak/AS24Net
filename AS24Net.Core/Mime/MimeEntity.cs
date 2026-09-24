using System.Security.Cryptography;
using System.Text;

namespace AS24Net.Core.Mime;

/// <summary>A header field of a MIME entity; the value is unfolded (continuation lines joined).</summary>
public sealed record MimeHeader(string Name, string Value);

/// <summary>
/// A MIME entity (RFC 2045): header fields and a body. An entity read from bytes keeps them
/// (<see cref="RawBytes"/>), because a signature and the MIC are computed over the exact bytes that were sent, not
/// over a re-serialisation that might order or fold the headers differently.
/// </summary>
public sealed class MimeEntity
{
    private static readonly Encoding HeaderEncoding = Encoding.Latin1;

    private MimeEntity(List<MimeHeader> headers, byte[] body, byte[]? rawBytes)
    {
        Headers = headers;
        Body = body;
        RawBytes = rawBytes;
    }

    public IReadOnlyList<MimeHeader> Headers { get; }

    /// <summary>The body as transferred, i.e. still in its <c>Content-Transfer-Encoding</c>.</summary>
    public byte[] Body { get; }

    /// <summary>The exact bytes the entity was read from, <c>null</c> for an entity built here.</summary>
    public byte[]? RawBytes { get; }

    /// <summary>The first header field of the name (case insensitive), <c>null</c> when there is none.</summary>
    public string? this[string name] =>
        Headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    public ContentType ContentType => ContentType.Parse(this["Content-Type"]);

    public string? TransferEncoding => this["Content-Transfer-Encoding"]?.Trim().ToLowerInvariant();

    /// <summary>The file name from <c>Content-Disposition</c> or the <c>name</c> parameter of the content type.</summary>
    public string? FileName
    {
        get
        {
            if (this["Content-Disposition"] is { } disposition)
            {
                var parameters = ContentType.Parse(disposition);
                if (parameters["filename"] is { Length: > 0 } fileName)
                    return fileName;
            }

            return ContentType["name"] is { Length: > 0 } name ? name : null;
        }
    }

    /// <summary>Creates an entity; headers are written in the given order, the body is taken as it is.</summary>
    public static MimeEntity Create(IEnumerable<MimeHeader> headers, byte[] body) => new(headers.ToList(), body, null);

    public static MimeEntity Create(string contentType, byte[] body, params (string Name, string Value)[] headers) =>
        Create(new[] { new MimeHeader("Content-Type", contentType) }.Concat(headers.Select(h => new MimeHeader(h.Name, h.Value))), body);

    /// <summary>
    /// An entity whose headers come from somewhere else, e.g. the HTTP request that carried the body: the body is
    /// what the headers describe, the headers are not part of <see cref="RawBytes"/>.
    /// </summary>
    public static MimeEntity FromHeaders(IEnumerable<KeyValuePair<string, string>> headers, byte[] body) =>
        new(headers.Select(h => new MimeHeader(h.Key, h.Value)).ToList(), body, null);

    /// <summary>
    /// Reads an entity. The header section ends at the first empty line; lines may end with CRLF or, from sloppy
    /// software, with LF alone.
    /// </summary>
    public static MimeEntity Parse(byte[] data)
    {
        var headers = new List<MimeHeader>();
        var position = 0;
        string? name = null;
        var value = new StringBuilder();

        while (position < data.Length)
        {
            var end = Array.IndexOf(data, (byte)'\n', position);
            var lineEnd = end < 0 ? data.Length : end;
            var next = end < 0 ? data.Length : end + 1;
            if (lineEnd > position && data[lineEnd - 1] == '\r')
                lineEnd--;

            if (lineEnd == position)
            {
                // The empty line: the body follows.
                position = next;
                break;
            }

            var line = HeaderEncoding.GetString(data, position, lineEnd - position);
            if ((line[0] == ' ' || line[0] == '\t') && name is not null)
            {
                value.Append(' ').Append(line.Trim());
            }
            else
            {
                if (name is not null)
                    headers.Add(new MimeHeader(name, value.ToString().Trim()));

                var colon = line.IndexOf(':');
                if (colon <= 0)
                    throw new MimeFormatException($"Invalid header line '{Shorten(line)}'.");

                name = line[..colon].Trim();
                value.Clear().Append(line[(colon + 1)..]);
            }

            position = next;
        }

        if (name is not null)
            headers.Add(new MimeHeader(name, value.ToString().Trim()));

        return new MimeEntity(headers, data[Math.Min(position, data.Length)..], data);
    }

    /// <summary>The entity as bytes: the ones it was read from, or its headers, an empty line and the body.</summary>
    public byte[] ToBytes()
    {
        if (RawBytes is not null)
            return RawBytes;

        using var stream = new MemoryStream();
        foreach (var header in Headers)
        {
            var line = HeaderEncoding.GetBytes($"{header.Name}: {header.Value}\r\n");
            stream.Write(line);
        }

        stream.Write("\r\n"u8);
        stream.Write(Body);
        return stream.ToArray();
    }

    /// <summary>The body decoded from its <c>Content-Transfer-Encoding</c> (base64, quoted-printable or none).</summary>
    public byte[] DecodeBody() => TransferEncoding switch
    {
        "base64" => DecodeBase64(Body),
        "quoted-printable" => QuotedPrintable.Decode(Body),
        _ => Body,
    };

    /// <summary>Decodes base64 that may be wrapped into lines.</summary>
    public static byte[] DecodeBase64(byte[] data)
    {
        var text = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t')
                continue;
            text.Append((char)b);
        }

        try
        {
            return Convert.FromBase64String(text.ToString());
        }
        catch (FormatException ex)
        {
            throw new MimeFormatException("The body is not valid base64: " + ex.Message, ex);
        }
    }

    /// <summary>Base64 in lines of 76 characters, as RFC 2045 wants it.</summary>
    public static byte[] EncodeBase64(byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        var builder = new StringBuilder(base64.Length + base64.Length / 76 * 2 + 2);
        for (var i = 0; i < base64.Length; i += 76)
            builder.Append(base64, i, Math.Min(76, base64.Length - i)).Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>
    /// The body parts of a multipart entity, each with the exact bytes it was sent in (RFC 2046 5.1.1: the line
    /// break in front of a boundary belongs to the boundary, not to the part).
    /// </summary>
    public IReadOnlyList<MimeEntity> GetParts()
    {
        var boundary = ContentType.Boundary;
        if (!ContentType.IsMultipart || string.IsNullOrEmpty(boundary))
            throw new MimeFormatException($"The entity of type {ContentType.MediaType} has no parts.");

        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var parts = new List<MimeEntity>();
        var body = Body;
        var start = FindDelimiter(body, delimiter, 0);
        if (start < 0)
            throw new MimeFormatException($"The boundary '{boundary}' does not occur in the body.");

        while (true)
        {
            var afterDelimiter = start + delimiter.Length;
            if (IsClosing(body, afterDelimiter))
                break;

            var partStart = SkipLine(body, afterDelimiter);
            var next = FindDelimiter(body, delimiter, partStart);
            if (next < 0)
                throw new MimeFormatException($"The multipart body is not closed with the boundary '{boundary}'.");

            var partEnd = next;
            if (partEnd > partStart && body[partEnd - 1] == '\n')
                partEnd--;
            if (partEnd > partStart && body[partEnd - 1] == '\r')
                partEnd--;

            parts.Add(Parse(body[partStart..Math.Max(partStart, partEnd)]));
            start = next;
        }

        return parts;
    }

    private static bool IsClosing(byte[] body, int position) =>
        position + 1 < body.Length && body[position] == '-' && body[position + 1] == '-';

    private static int SkipLine(byte[] body, int position)
    {
        var end = Array.IndexOf(body, (byte)'\n', position);
        return end < 0 ? body.Length : end + 1;
    }

    /// <summary>The next delimiter that starts a line.</summary>
    private static int FindDelimiter(byte[] body, byte[] delimiter, int from)
    {
        var span = body.AsSpan();
        var position = from;
        while (position <= body.Length - delimiter.Length)
        {
            var index = span[position..].IndexOf(delimiter);
            if (index < 0)
                return -1;

            index += position;
            if (index == 0 || body[index - 1] == '\n')
                return index;

            position = index + 1;
        }

        return -1;
    }

    /// <summary>A boundary that does not occur in any of the given contents.</summary>
    public static string NewBoundary() => "----=_Part_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    /// <summary>
    /// A multipart entity of the parts; the boundary and the media type are put into <paramref name="contentType"/>.
    /// </summary>
    public static MimeEntity CreateMultipart(ContentType contentType, IReadOnlyList<byte[]> parts, params (string Name, string Value)[] headers)
    {
        var boundary = NewBoundary();
        contentType.Parameters["boundary"] = boundary;

        using var body = new MemoryStream();
        foreach (var part in parts)
        {
            body.Write(Encoding.ASCII.GetBytes($"--{boundary}\r\n"));
            body.Write(part);
            body.Write("\r\n"u8);
        }

        body.Write(Encoding.ASCII.GetBytes($"--{boundary}--\r\n"));
        return Create(contentType.ToString(), body.ToArray(), headers);
    }

    private static string Shorten(string value) => value.Length > 60 ? value[..60] + "…" : value;
}

public class MimeFormatException(string message, Exception? innerException = null) : Exception(message, innerException);
