namespace AS24Net.Interop;

public enum MdnKind
{
    None,
    Sync,
    Async,
}

/// <summary>
/// One combination of security options, tested in both directions with every station. The algorithm names are
/// those of AS24Net (<c>sha-256</c>, <c>aes256-cbc</c>, …); every station translates them into its own.
/// </summary>
public sealed record InteropCase(
    string Id,
    bool Sign = true,
    string Digest = "sha-256",
    bool Encrypt = true,
    string Encryption = "aes256-cbc",
    bool Compress = false,
    bool CompressBeforeSigning = true,
    MdnKind Mdn = MdnKind.Sync,
    bool SignedMdn = true)
{
    public static readonly IReadOnlyList<InteropCase> All =
    [
        new("plain", Sign: false, Encrypt: false, SignedMdn: false),
        new("signed", Encrypt: false),
        new("encrypted", Sign: false, SignedMdn: false),
        new("sha1-aes256", Digest: "sha1"),
        new("sha256-aes256"),
        new("sha384-aes256", Digest: "sha-384"),
        new("sha512-aes256", Digest: "sha-512"),
        new("sha256-aes128", Encryption: "aes128-cbc"),
        new("sha256-aes192", Encryption: "aes192-cbc"),
        new("sha256-3des", Encryption: "3des"),
        new("compressed", Compress: true),
        new("compressed-after-signing", Compress: true, CompressBeforeSigning: false),
        new("async-mdn", Mdn: MdnKind.Async),
        new("async-unsigned-mdn", Mdn: MdnKind.Async, SignedMdn: false),
        new("no-mdn", Mdn: MdnKind.None, SignedMdn: false),
    ];

    /// <summary>The digest of the MDN's signature and the MIC: the one of the message, SHA-256 without a signature.</summary>
    public string MdnDigest => Sign ? Digest : "sha-256";

    public override string ToString() => Id;
}

public enum Direction
{
    /// <summary>AS24Net sends, the station receives.</summary>
    Outbound,

    /// <summary>The station sends, AS24Net receives.</summary>
    Inbound,
}
