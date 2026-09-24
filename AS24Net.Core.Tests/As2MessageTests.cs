using System.Text;
using AS24Net.Core.As2;
using AS24Net.Core.Security;
using static AS24Net.Core.Tests.TestCertificates;

namespace AS24Net.Core.Tests;

/// <summary>Alice sends to Bob: every combination of the security options survives the way there.</summary>
public class As2MessageTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("UNA:+.? 'UNB+UNOC:3+ALICE+BOB+260924:1015+1'\r\nUNZ+1+1'\r\n" + new string('x', 5000));

    public static TheoryData<bool, bool, bool, bool> Combinations()
    {
        var data = new TheoryData<bool, bool, bool, bool>();
        foreach (var sign in new[] { false, true })
        foreach (var encrypt in new[] { false, true })
        foreach (var compress in new[] { false, true })
        foreach (var compressBeforeSigning in new[] { false, true })
            data.Add(sign, encrypt, compress, compressBeforeSigning);
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Message_IsReadAsItWasSent_AndTheMicsMatch(bool sign, bool encrypt, bool compress, bool compressBeforeSigning)
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE AS2",
            To = "BOB",
            Payload = Payload,
            ContentType = "application/edifact",
            FileName = "orders.edi",
            Compress = compress,
            CompressBeforeSigning = compressBeforeSigning,
            SigningCertificate = sign ? Alice : null,
            EncryptionCertificate = encrypt ? Public(Bob) : null,
            Mdn = new MdnRequest { NotificationTo = "alice@example.com", Signed = true, MicAlgorithms = [As2Algorithms.Sha256] },
        });

        Assert.Equal("\"ALICE AS2\"", message.Headers[As2Headers.As2From]);
        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Alice)]);

        Assert.Equal(Payload, received.Payload);
        Assert.Equal("application/edifact", received.ContentType);
        Assert.Equal("orders.edi", received.FileName);
        Assert.Equal(sign, received.Signed);
        Assert.Equal(encrypt, received.Encrypted);
        Assert.Equal(compress, received.Compressed);
        Assert.Equal(message.Mic, received.Mic);
    }

    [Fact]
    public void UnsignedWithoutMime_EnvelopesThePayloadItself_AndIsReadBack()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, ContentType = "application/edifact", FileName = "orders.edi",
            EncryptionCertificate = Public(Bob), UnsignedWithoutMime = true,
        });

        // What Mendelson AS2 sends and expects: no MIME headers inside the envelope.
        Assert.Equal(Payload, Smime.Decrypt(message.Body, [Bob]).Content);

        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Alice)]);
        Assert.Equal(Payload, received.Payload);
        Assert.Equal("application/octet-stream", received.ContentType);
        Assert.True(received.Encrypted);
        Assert.False(received.Signed);
        Assert.Equal(message.Mic, received.Mic);
        Assert.Equal(Mic.Compute(Payload, As2Algorithms.Sha256), received.Mic);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnsignedWithoutMime_DoesNotChangeASignedOrCompressedMessage(bool sign, bool compress)
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, ContentType = "application/edifact",
            SigningCertificate = sign ? Alice : null, Compress = compress,
            EncryptionCertificate = Public(Bob), UnsignedWithoutMime = true,
        });

        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Alice)]);
        Assert.Equal(Payload, received.Payload);
        Assert.Equal("application/edifact", received.ContentType);
    }

    [Fact]
    public void Signature_WithAnUnknownCertificate_IsRefused()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "MALLORY", To = "BOB", Payload = Payload, SigningCertificate = Mallory,
        });

        var error = Assert.Throws<As2ProcessingException>(() => As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Alice)]));
        Assert.Equal(As2Errors.AuthenticationFailed, error.Error);
    }

    [Fact]
    public void Signature_IsVerifiedWithThePreviousCertificateDuringARollOver()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, SigningCertificate = Alice,
        });

        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Mallory), Public(Alice)]);

        Assert.Equal(Alice.Thumbprint, received.SignatureCertificate!.Thumbprint);
    }

    [Fact]
    public void ChangedContent_FailsTheIntegrityCheck()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Encoding.ASCII.GetBytes("amount=100"), SigningCertificate = Alice,
        });
        var body = Encoding.Latin1.GetString(message.Body).Replace("amount=100", "amount=900");

        var error = Assert.Throws<As2ProcessingException>(() =>
            As2MessageReader.Read(message.Headers, Encoding.Latin1.GetBytes(body), [Bob], [Public(Alice)]));
        Assert.Equal(As2Errors.IntegrityCheckFailed, error.Error);
    }

    [Fact]
    public void Message_EncryptedForSomebodyElse_CannotBeDecrypted()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, EncryptionCertificate = Public(Mallory),
        });

        var error = Assert.Throws<As2ProcessingException>(() => As2MessageReader.Read(message.Headers, message.Body, [Bob], []));
        Assert.Equal(As2Errors.DecryptionFailed, error.Error);
    }

    [Fact]
    public void EncryptedBody_InBase64_IsAccepted()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, EncryptionCertificate = Public(Bob),
        });
        var headers = new Dictionary<string, string>(message.Headers, StringComparer.OrdinalIgnoreCase)
        {
            [As2Headers.ContentTransferEncoding] = "base64",
        };

        var received = As2MessageReader.Read(headers, Encoding.ASCII.GetBytes(Convert.ToBase64String(message.Body)), [Bob], []);

        Assert.Equal(Payload, received.Payload);
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("sha-384")]
    [InlineData("sha-512")]
    public void Mic_OfASignedMessage_UsesTheDigestOfTheSignature(string algorithm)
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, SigningCertificate = Alice, SignatureAlgorithm = algorithm,
            Mdn = new MdnRequest { NotificationTo = "x", Signed = true, MicAlgorithms = [As2Algorithms.Sha256] },
        });

        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], [Public(Alice)]);

        Assert.EndsWith(", " + algorithm, received.Mic);
        Assert.Equal(algorithm, received.SignatureAlgorithm);
        Assert.True(Mic.AreEqual(message.Mic, received.Mic));
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("sha-512")]
    public void Mic_OfAnUnsignedMessage_UsesTheAlgorithmTheMdnAsksFor(string algorithm)
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = "ALICE", To = "BOB", Payload = Payload, EncryptionCertificate = Public(Bob),
            Mdn = new MdnRequest { NotificationTo = "x", Signed = true, MicAlgorithms = ["unknown", algorithm] },
        });

        var received = As2MessageReader.Read(message.Headers, message.Body, [Bob], []);

        Assert.EndsWith(", " + algorithm, received.Mic);
        Assert.True(Mic.AreEqual(message.Mic, received.Mic));
    }

    [Fact]
    public void MdnRequest_IsReadFromTheHeaders()
    {
        var headers = As2Headers.NewCollection();
        headers["disposition-notification-to"] = "mailto:as2@example.com";
        headers["Disposition-Notification-Options"] = "signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, sha256, sha1";
        headers["Receipt-Delivery-Option"] = "https://alice.example.com/as2";

        var request = MdnRequest.FromHeaders(headers)!;

        Assert.True(request.Signed);
        Assert.True(request.IsAsync);
        Assert.Equal("https://alice.example.com/as2", request.ReturnUrl);
        Assert.Equal(As2Algorithms.Sha256, request.MicAlgorithm(As2Algorithms.Sha1));
    }

    [Fact]
    public void NoMdnRequested_WithoutDispositionNotificationTo()
    {
        Assert.Null(MdnRequest.FromHeaders(As2Headers.NewCollection()));
    }

    [Theory]
    [InlineData("BOB", "BOB")]
    [InlineData("Bob Company", "\"Bob Company\"")]
    public void As2Id_IsQuotedWhenNeeded(string id, string expected)
    {
        Assert.Equal(expected, As2Headers.QuoteAs2Id(id));
        Assert.Equal(id, As2Headers.UnquoteAs2Id(expected));
    }
}
