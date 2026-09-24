using AS24Net.Core.Mime;
using AS24Net.Core.Security;

namespace AS24Net.Core.As2;

/// <summary>
/// What the sender of a message asked for in its headers: whether an MDN is wanted at all
/// (<c>Disposition-Notification-To</c>), whether it is to come back in the HTTP response (synchronous) or later to
/// a URL (<c>Receipt-Delivery-Option</c>, asynchronous), and whether it is to be signed and with which digest
/// (<c>Disposition-Notification-Options</c>, RFC 4130 7.3).
/// </summary>
public sealed class MdnRequest
{
    /// <summary>Value of <c>Disposition-Notification-To</c>; RFC 4130 does not use it beyond "an MDN is wanted".</summary>
    public string NotificationTo { get; init; } = "";

    /// <summary>URL the MDN is to be sent to; <c>null</c> for a synchronous MDN.</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>The MDN is to be signed.</summary>
    public bool Signed { get; init; }

    /// <summary>Digest algorithms for the signature of the MDN and the MIC, in the order of preference.</summary>
    public IReadOnlyList<string> MicAlgorithms { get; init; } = [];

    public bool IsAsync => !string.IsNullOrWhiteSpace(ReturnUrl);

    /// <summary>The first digest algorithm asked for that is supported, otherwise <paramref name="fallback"/>.</summary>
    public string MicAlgorithm(string fallback) =>
        MicAlgorithms.Select(As2Algorithms.NormalizeDigest).FirstOrDefault(a => a is not null) ?? fallback;

    /// <summary>The request of a message from its headers, <c>null</c> when no MDN is wanted.</summary>
    public static MdnRequest? FromHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(As2Headers.DispositionNotificationTo, out var to) || string.IsNullOrWhiteSpace(to))
            return null;

        headers.TryGetValue(As2Headers.ReceiptDeliveryOption, out var returnUrl);
        headers.TryGetValue(As2Headers.DispositionNotificationOptions, out var options);
        var (signed, algorithms) = ParseOptions(options);

        return new MdnRequest
        {
            NotificationTo = to.Trim(),
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl.Trim(),
            Signed = signed,
            MicAlgorithms = algorithms,
        };
    }

    /// <summary>
    /// Reads <c>signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, sha-256, sha1</c>.
    /// A signed MDN is asked for when the protocol is pkcs7-signature, whether it is optional or required.
    /// </summary>
    public static (bool Signed, IReadOnlyList<string> MicAlgorithms) ParseOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return (false, []);

        var signed = false;
        var algorithms = new List<string>();
        foreach (var part in ContentType.SplitParameters("x/x;" + options).Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            var name = part[..equals].Trim();
            var values = part[(equals + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ContentType.Unquote)
                .ToList();

            // The first value is the importance (required / optional), the rest the values.
            var list = values.Count > 0 && values[0] is "required" or "optional" ? values.Skip(1).ToList() : values;
            if (name.Equals("signed-receipt-protocol", StringComparison.OrdinalIgnoreCase))
                signed = list.Any(v => v.Equals("pkcs7-signature", StringComparison.OrdinalIgnoreCase));
            else if (name.Equals("signed-receipt-micalg", StringComparison.OrdinalIgnoreCase))
                algorithms.AddRange(list);
        }

        return (signed, algorithms);
    }

    /// <summary>The value of <c>Disposition-Notification-Options</c> asking for a signed MDN.</summary>
    public static string FormatOptions(string micAlgorithm) =>
        $"signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, {micAlgorithm}";
}
