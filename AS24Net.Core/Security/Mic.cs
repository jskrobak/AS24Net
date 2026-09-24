using System.Security.Cryptography;

namespace AS24Net.Core.Security;

/// <summary>
/// Message Integrity Check (RFC 4130 7.3.1): the digest of the received content, returned by the receiver in the
/// MDN as <c>Received-Content-MIC: &lt;base64&gt;, &lt;algorithm&gt;</c> and compared by the sender with the one it computed.
/// </summary>
public static class Mic
{
    /// <summary>The value of <c>Received-Content-MIC</c> for the content.</summary>
    public static string Compute(byte[] content, string algorithm)
    {
        var digest = As2Algorithms.NormalizeDigest(algorithm)
                     ?? throw new ArgumentException($"Digest algorithm '{algorithm}' is not supported.", nameof(algorithm));
        var hash = CryptographicOperations.HashData(As2Algorithms.HashAlgorithm(digest), content);
        return $"{Convert.ToBase64String(hash)}, {digest}";
    }

    /// <summary>Splits a MIC into the base64 digest and the algorithm (normalized); <c>null</c> when it is malformed.</summary>
    public static (string Digest, string Algorithm)? Parse(string? mic)
    {
        if (string.IsNullOrWhiteSpace(mic))
            return null;

        var comma = mic.LastIndexOf(',');
        if (comma <= 0)
            return null;

        var algorithm = As2Algorithms.NormalizeDigest(mic[(comma + 1)..]);
        return algorithm is null ? null : (mic[..comma].Trim(), algorithm);
    }

    /// <summary>Two MICs are equal when their digests and algorithms are, whatever the spelling of the algorithm.</summary>
    public static bool AreEqual(string? a, string? b) =>
        Parse(a) is { } first && Parse(b) is { } second
        && first.Algorithm == second.Algorithm
        && string.Equals(first.Digest, second.Digest, StringComparison.Ordinal);
}
