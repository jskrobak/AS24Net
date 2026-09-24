using AS24Net.Core.As2;
using AS24Net.Core.Security;
using static AS24Net.Core.Tests.TestCertificates;

namespace AS24Net.Core.Tests;

public class MdnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mdn_IsReadAsItWasBuilt(bool signed)
    {
        var mdn = MdnProcessor.Build(new MdnOptions
        {
            From = "BOB",
            To = "ALICE",
            OriginalMessageId = "<1@alice>",
            Disposition = Mdn.ProcessedDisposition,
            ReceivedContentMic = "abc=, sha-256",
            SigningCertificate = signed ? Bob : null,
        });

        Assert.True(MdnProcessor.IsMdn(mdn.Headers, mdn.Body));
        var read = MdnProcessor.Read(mdn.Headers, mdn.Body, [Public(Bob)], requireSignature: signed);

        Assert.Equal("<1@alice>", read.OriginalMessageId);
        Assert.Equal("abc=, sha-256", read.ReceivedContentMic);
        Assert.Equal("BOB", read.From);
        Assert.True(read.IsPositive);
        Assert.Equal(signed, read.Signed);
        Assert.Contains("received", read.Text);
    }

    [Fact]
    public void UnsignedMdn_IsRefusedWhenASignedOneWasRequested()
    {
        var mdn = MdnProcessor.Build(new MdnOptions { From = "BOB", To = "ALICE", OriginalMessageId = "<1@a>", Disposition = Mdn.ProcessedDisposition });

        var error = Assert.Throws<As2ProcessingException>(() => MdnProcessor.Read(mdn.Headers, mdn.Body, [Public(Bob)], requireSignature: true));
        Assert.Equal(As2Errors.InsufficientMessageSecurity, error.Error);
    }

    [Fact]
    public void SignedMdn_OfSomebodyElse_IsRefused()
    {
        var mdn = MdnProcessor.Build(new MdnOptions
        {
            From = "BOB", To = "ALICE", OriginalMessageId = "<1@a>", Disposition = Mdn.ProcessedDisposition, SigningCertificate = Mallory,
        });

        Assert.Throws<As2ProcessingException>(() => MdnProcessor.Read(mdn.Headers, mdn.Body, [Public(Bob)], requireSignature: true));
    }

    [Theory]
    [InlineData("automatic-action/MDN-sent-automatically; processed", null)]
    [InlineData("automatic-action/MDN-sent-automatically; processed/warning: duplicate-document", null)]
    [InlineData("automatic-action/MDN-sent-automatically; processed/error: decryption-failed", "error: decryption-failed")]
    [InlineData("automatic-action/MDN-sent-automatically; failed/failure: sender-equals-receiver", "failed/failure: sender-equals-receiver")]
    public void Problem_IsTheModifierOfANegativeDisposition(string disposition, string? problem)
    {
        var mdn = new Mdn { Disposition = disposition };

        Assert.Equal(problem, mdn.Problem);
        Assert.Equal(problem is null, mdn.IsPositive);
    }

    [Fact]
    public void Message_IsNotAnMdn()
    {
        var message = As2MessageBuilder.Build(new As2OutboundOptions { From = "A", To = "B", Payload = [1, 2, 3], SigningCertificate = Alice });

        Assert.False(MdnProcessor.IsMdn(message.Headers, message.Body));
    }

    [Fact]
    public void ErrorDisposition_ContainsTheError()
    {
        Assert.Equal("automatic-action/MDN-sent-automatically; processed/error: authentication-failed",
            Mdn.ErrorDisposition(As2Errors.AuthenticationFailed));
    }
}
