using System.Text;
using AS24Net.Core.As2;
using AS24Net.Core.Security;
using static AS24Net.Core.Tests.TestCertificates;

namespace AS24Net.Core.Tests;

public class SecurityTests
{
    [Fact]
    public void Compression_RoundTrips()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ORDERS;", 1000)));

        var compressed = CmsCompression.Compress(data);

        Assert.True(compressed.Length < data.Length / 10);
        Assert.Equal(data, CmsCompression.Decompress(compressed));
    }

    [Fact]
    public void Decompression_OfSomethingElse_Fails()
    {
        var error = Assert.Throws<As2ProcessingException>(() => CmsCompression.Decompress([1, 2, 3]));
        Assert.Equal(As2Errors.DecompressionFailed, error.Error);
    }

    [Theory]
    [InlineData(As2Algorithms.TripleDes)]
    [InlineData(As2Algorithms.Aes128)]
    [InlineData(As2Algorithms.Aes192)]
    [InlineData(As2Algorithms.Aes256)]
    public void Encryption_RoundTrips(string algorithm)
    {
        var data = Encoding.ASCII.GetBytes("secret");

        var (content, used) = Smime.Decrypt(Smime.Encrypt(data, Public(Bob), algorithm), [Alice, Bob]);

        Assert.Equal(data, content);
        Assert.Equal(algorithm, used);
    }

    [Fact]
    public void Mic_IsTheBase64DigestWithTheAlgorithm()
    {
        // SHA-256 of "abc".
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=, sha-256", Mic.Compute("abc"u8.ToArray(), "SHA256"));
        Assert.True(Mic.AreEqual("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=, sha256", "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0= , SHA-256"));
        Assert.False(Mic.AreEqual("a=, sha-256", "a=, sha1"));
    }

    [Theory]
    [InlineData("SHA256", As2Algorithms.Sha256)]
    [InlineData("sha-1", As2Algorithms.Sha1)]
    [InlineData("md5", null)]
    public void DigestNames_AreNormalized(string name, string? expected)
    {
        Assert.Equal(expected, As2Algorithms.NormalizeDigest(name));
    }

    [Fact]
    public void Canonicalize_TurnsBareLineFeedsIntoCrLf()
    {
        Assert.Equal("a\r\nb\r\nc", Encoding.ASCII.GetString(As2MessageReader.Canonicalize("a\nb\r\nc"u8.ToArray())));
    }
}
