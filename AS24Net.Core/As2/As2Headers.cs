using System.Security.Cryptography;
using System.Text;

namespace AS24Net.Core.As2;

/// <summary>HTTP headers of AS2 (RFC 4130 6) and helpers for their values.</summary>
public static class As2Headers
{
    public const string As2Version = "AS2-Version";
    public const string As2From = "AS2-From";
    public const string As2To = "AS2-To";
    public const string MessageId = "Message-ID";
    public const string Subject = "Subject";
    public const string Date = "Date";
    public const string MimeVersion = "MIME-Version";
    public const string ContentType = "Content-Type";
    public const string ContentTransferEncoding = "Content-Transfer-Encoding";
    public const string ContentDisposition = "Content-Disposition";
    public const string DispositionNotificationTo = "Disposition-Notification-To";
    public const string DispositionNotificationOptions = "Disposition-Notification-Options";
    public const string ReceiptDeliveryOption = "Receipt-Delivery-Option";
    public const string EdiintFeatures = "EDIINT-Features";
    public const string UserAgent = "User-Agent";
    public const string Server = "Server";

    /// <summary>AS2 version 1.2: compression (RFC 5402) is supported.</summary>
    public const string Version = "1.2";

    /// <summary>
    /// Features announced to partners: duplicates are recognised by their Message-ID (AS2 reliability).
    /// </summary>
    public const string Features = "AS2-Reliability";

    public const string Product = "AS24Net";

    /// <summary>Headers are compared case insensitively; this is the dictionary to keep them in.</summary>
    public static Dictionary<string, string> NewCollection() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An AS2 name as it is sent: in quotes when it contains a space or another character not allowed in a token
    /// (RFC 4130 6.2: 1 - 128 printable ASCII characters).
    /// </summary>
    public static string QuoteAs2Id(string as2Id)
    {
        var value = as2Id.Trim();
        return value.Any(c => c <= ' ' || "()<>@,;:\\\"/[]?={}".Contains(c))
            ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value;
    }

    /// <summary>The AS2 name from a header value, without quotes.</summary>
    public static string UnquoteAs2Id(string? value) => Mime.ContentType.Unquote(value?.Trim() ?? "");

    /// <summary>Checks an AS2 name: 1 to 128 printable ASCII characters.</summary>
    public static bool IsValidAs2Id(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 128 && value.Trim().All(c => c >= ' ' && c < 127);

    /// <summary>A new unique Message-ID (RFC 5322 msg-id), e.g. <c>&lt;AS24Net-20260924101500123-1a2b3c4d@host&gt;</c>.</summary>
    public static string NewMessageId(string? host = null)
    {
        var domain = string.IsNullOrWhiteSpace(host) ? "as24net" : host.Trim();
        return $"<{Product}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}@{domain}>";
    }

    /// <summary>A Message-ID in angle brackets, whichever way it came.</summary>
    public static string NormalizeMessageId(string? value)
    {
        var id = value?.Trim() ?? "";
        if (id.Length == 0)
            return id;
        return id.StartsWith('<') ? id : $"<{id.TrimEnd('>')}>";
    }

    /// <summary>The current time as the <c>Date</c> header wants it (RFC 5322).</summary>
    public static string Now() => DateTimeOffset.UtcNow.ToString("r");

    /// <summary>A header value made of printable ASCII, e.g. a subject taken from a file name.</summary>
    public static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(c is >= ' ' and < (char)127 ? c : '_');
        return builder.ToString();
    }
}
