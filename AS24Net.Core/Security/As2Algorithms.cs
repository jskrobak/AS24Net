using System.Security.Cryptography;

namespace AS24Net.Core.Security;

/// <summary>
/// Names of the algorithms as they appear in AS2 headers (<c>micalg</c>, <c>signed-receipt-micalg</c>) and in the
/// configuration of a partner, and their object identifiers.
/// </summary>
public static class As2Algorithms
{
    public const string Sha1 = "sha1";
    public const string Sha256 = "sha-256";
    public const string Sha384 = "sha-384";
    public const string Sha512 = "sha-512";

    public const string TripleDes = "3des";
    public const string Aes128 = "aes128-cbc";
    public const string Aes192 = "aes192-cbc";
    public const string Aes256 = "aes256-cbc";

    /// <summary>Digest algorithms for signatures and the MIC, the strongest first.</summary>
    public static readonly IReadOnlyList<string> DigestAlgorithms = [Sha256, Sha384, Sha512, Sha1];

    /// <summary>Content encryption algorithms, the recommended first.</summary>
    public static readonly IReadOnlyList<string> EncryptionAlgorithms = [Aes256, Aes192, Aes128, TripleDes];

    /// <summary>
    /// The name of a digest algorithm in the spelling of RFC 5751 (<c>sha-256</c>), from any of the spellings used
    /// by AS2 software (<c>sha256</c>, <c>SHA-256</c>, <c>sha-1</c>); <c>null</c> when it is not supported.
    /// </summary>
    public static string? NormalizeDigest(string? name) => name?.Trim().ToLowerInvariant().Replace("_", "-") switch
    {
        "sha1" or "sha-1" => Sha1,
        "sha256" or "sha-256" => Sha256,
        "sha384" or "sha-384" => Sha384,
        "sha512" or "sha-512" => Sha512,
        _ => null,
    };

    public static HashAlgorithmName HashAlgorithm(string digest) => NormalizeDigest(digest) switch
    {
        Sha1 => HashAlgorithmName.SHA1,
        Sha256 => HashAlgorithmName.SHA256,
        Sha384 => HashAlgorithmName.SHA384,
        Sha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentException($"Digest algorithm '{digest}' is not supported.", nameof(digest)),
    };

    public static Oid DigestOid(string digest) => NormalizeDigest(digest) switch
    {
        Sha1 => new Oid("1.3.14.3.2.26"),
        Sha256 => new Oid("2.16.840.1.101.3.4.2.1"),
        Sha384 => new Oid("2.16.840.1.101.3.4.2.2"),
        Sha512 => new Oid("2.16.840.1.101.3.4.2.3"),
        _ => throw new ArgumentException($"Digest algorithm '{digest}' is not supported.", nameof(digest)),
    };

    /// <summary>The AS2 name of the digest algorithm with the object identifier, <c>null</c> when it is unknown.</summary>
    public static string? DigestName(Oid? oid) => oid?.Value switch
    {
        "1.3.14.3.2.26" => Sha1,
        "2.16.840.1.101.3.4.2.1" => Sha256,
        "2.16.840.1.101.3.4.2.2" => Sha384,
        "2.16.840.1.101.3.4.2.3" => Sha512,
        _ => null,
    };

    public static string? NormalizeEncryption(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "3des" or "des-ede3-cbc" or "tripledes" => TripleDes,
        "aes128" or "aes128-cbc" or "aes-128-cbc" => Aes128,
        "aes192" or "aes192-cbc" or "aes-192-cbc" => Aes192,
        "aes256" or "aes256-cbc" or "aes-256-cbc" => Aes256,
        _ => null,
    };

    public static Oid EncryptionOid(string algorithm) => NormalizeEncryption(algorithm) switch
    {
        TripleDes => new Oid("1.2.840.113549.3.7"),
        Aes128 => new Oid("2.16.840.1.101.3.4.1.2"),
        Aes192 => new Oid("2.16.840.1.101.3.4.1.22"),
        Aes256 => new Oid("2.16.840.1.101.3.4.1.42"),
        _ => throw new ArgumentException($"Encryption algorithm '{algorithm}' is not supported.", nameof(algorithm)),
    };

    public static string? EncryptionName(Oid? oid) => oid?.Value switch
    {
        "1.2.840.113549.3.7" => TripleDes,
        "2.16.840.1.101.3.4.1.2" => Aes128,
        "2.16.840.1.101.3.4.1.22" => Aes192,
        "2.16.840.1.101.3.4.1.42" => Aes256,
        _ => null,
    };
}
