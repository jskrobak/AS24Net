using AS24Net.Core.As2;
using AS24Net.Domain;
using AS24Net.Services.As2;

namespace AS24Net.Services.Tests;

public class MdnEvaluationTests
{
    private const string OurMic = "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=, sha-256";

    private static OutgoingMessage Message(bool signed = true) => new() { Mic = OurMic, Signed = signed };

    [Fact]
    public void PositiveMdn_WithOurMic_DeliversTheMessage()
    {
        var message = Message();
        var outcome = MdnEvaluation.Apply(message, new Mdn { Disposition = Mdn.ProcessedDisposition, ReceivedContentMic = "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=, sha256" },
            new DateTime(2026, 9, 24, 12, 0, 0));

        Assert.True(outcome.Delivered);
        Assert.Null(outcome.Warning);
        Assert.Equal(OutgoingStatus.Delivered, message.Status);
        Assert.Equal(new DateTime(2026, 9, 24, 12, 0, 0), message.DeliveredDate);
    }

    [Fact]
    public void NegativeMdn_DoesNotDeliverTheMessage()
    {
        var message = Message();
        var outcome = MdnEvaluation.Apply(message, new Mdn { Disposition = Mdn.ErrorDisposition(As2Errors.DecryptionFailed) }, DateTime.Now);

        Assert.False(outcome.Delivered);
        Assert.Equal(TransferEventType.MessageNotDelivered, outcome.FailureType);
        Assert.Equal(OutgoingStatus.NotDelivered, message.Status);
        Assert.Contains(As2Errors.DecryptionFailed, message.LastError);
    }

    [Fact]
    public void OtherMic_OfASignedMessage_IsAFailure()
    {
        var outcome = MdnEvaluation.Evaluate(Message(), new Mdn { Disposition = Mdn.ProcessedDisposition, ReceivedContentMic = "AAAA, sha-256" });

        Assert.False(outcome.Delivered);
        Assert.Equal(TransferEventType.MicMismatch, outcome.FailureType);
    }

    [Fact]
    public void OtherMic_OfAnUnsignedMessage_IsAWarning()
    {
        var outcome = MdnEvaluation.Evaluate(Message(signed: false), new Mdn { Disposition = Mdn.ProcessedDisposition, ReceivedContentMic = "AAAA, sha-256" });

        Assert.True(outcome.Delivered);
        Assert.NotNull(outcome.Warning);
    }

    [Fact]
    public void MicInAnotherAlgorithm_CannotBeCompared_AndIsAWarning()
    {
        var outcome = MdnEvaluation.Evaluate(Message(), new Mdn { Disposition = Mdn.ProcessedDisposition, ReceivedContentMic = "AAAA, sha1" });

        Assert.True(outcome.Delivered);
        Assert.Contains("cannot be compared", outcome.Warning);
    }

    [Fact]
    public void MdnWithAWarning_StillDelivers()
    {
        var outcome = MdnEvaluation.Evaluate(Message(), new Mdn { Disposition = Mdn.WarningDisposition("duplicate-document"), ReceivedContentMic = OurMic });

        Assert.True(outcome.Delivered);
    }
}
