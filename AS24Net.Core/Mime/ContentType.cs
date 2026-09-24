using System.Text;

namespace AS24Net.Core.Mime;

/// <summary>
/// A MIME <c>Content-Type</c> value (RFC 2045): the media type and its parameters, e.g.
/// <c>multipart/signed; protocol="application/pkcs7-signature"; micalg=sha-256; boundary="----=_Part_1"</c>.
/// </summary>
public sealed class ContentType
{
    public ContentType(string mediaType, IEnumerable<KeyValuePair<string, string>>? parameters = null)
    {
        MediaType = mediaType.Trim().ToLowerInvariant();
        if (parameters is not null)
            foreach (var (name, value) in parameters)
                Parameters[name] = value;
    }

    /// <summary>Type and subtype in lower case, e.g. <c>application/pkcs7-mime</c>.</summary>
    public string MediaType { get; }

    /// <summary>Parameters by their name, which is case insensitive; values are unquoted.</summary>
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string parameter] => Parameters.TryGetValue(parameter, out var value) ? value : null;

    public string? Boundary => this["boundary"];

    public bool Is(string mediaType) => string.Equals(MediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    public bool IsMultipart => MediaType.StartsWith("multipart/", StringComparison.Ordinal);

    /// <summary>S/MIME package: <c>application/pkcs7-mime</c> or its old name <c>application/x-pkcs7-mime</c>.</summary>
    public bool IsPkcs7Mime => Is("application/pkcs7-mime") || Is("application/x-pkcs7-mime");

    /// <summary>Detached signature: <c>application/pkcs7-signature</c> or <c>application/x-pkcs7-signature</c>.</summary>
    public bool IsPkcs7Signature => Is("application/pkcs7-signature") || Is("application/x-pkcs7-signature");

    public static ContentType Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ContentType("text/plain");

        var parts = SplitParameters(value);
        var result = new ContentType(parts[0]);
        foreach (var part in parts.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            var name = part[..equals].Trim();
            var parameterValue = part[(equals + 1)..].Trim();
            result.Parameters[name] = Unquote(parameterValue);
        }

        return result;
    }

    /// <summary>Splits at semicolons outside of quoted strings.</summary>
    internal static List<string> SplitParameters(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && quoted && i + 1 < value.Length)
            {
                current.Append(c).Append(value[++i]);
                continue;
            }

            if (c == '"')
                quoted = !quoted;

            if (c == ';' && !quoted)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        parts.Add(current.ToString().Trim());
        return parts.Where(p => p.Length > 0).DefaultIfEmpty("text/plain").ToList();
    }

    internal static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return value;

        var inner = value[1..^1];
        var result = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
                i++;
            result.Append(inner[i]);
        }

        return result.ToString();
    }

    /// <summary>Quotes a parameter value when it contains characters that are not allowed in a token (RFC 2045).</summary>
    public static string Quote(string value) =>
        value.Length > 0 && value.All(c => c > ' ' && c < 127 && !"()<>@,;:\\\"/[]?=".Contains(c))
            ? value
            : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public override string ToString()
    {
        var builder = new StringBuilder(MediaType);
        foreach (var (name, value) in Parameters)
            builder.Append("; ").Append(name).Append('=').Append(Quote(value));
        return builder.ToString();
    }
}
