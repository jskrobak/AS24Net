using AS24Net.Core.As2;
using AS24Net.Core.Security;
using AS24Net.Domain;

namespace AS24Net.Services.As2;

/// <summary>What an MDN means for the message it confirms.</summary>
/// <param name="Delivered">The partner received and processed the message.</param>
/// <param name="FailureType">Why it was not delivered, for the transfer log.</param>
/// <param name="Problem">Why it was not delivered, in words.</param>
/// <param name="Warning">Delivered, but something is worth a look (e.g. an unsigned message with another MIC).</param>
public sealed record MdnOutcome(bool Delivered, TransferEventType? FailureType, string? Problem, string? Warning);

public static class MdnEvaluation
{
    /// <summary>
    /// A positive MDN delivers the message when its MIC is ours. For a signed message a different MIC means the
    /// partner confirmed other content than we sent, so the message is not delivered; for an unsigned one the MIC
    /// is computed differently by some software, so it is only a warning. So is a MIC in another algorithm than ours,
    /// which cannot be compared.
    /// </summary>
    public static MdnOutcome Evaluate(OutgoingMessage message, Mdn mdn)
    {
        if (mdn.Problem is { } problem)
            return new MdnOutcome(false, TransferEventType.MessageNotDelivered, $"the MDN reports {problem}", null);

        if (mdn.ReceivedContentMic is null)
            return new MdnOutcome(true, null, null, message.Signed ? "the MDN contains no MIC" : null);

        if (Mic.Parse(mdn.ReceivedContentMic) is { } received && Mic.Parse(message.Mic) is { } ours && received.Algorithm != ours.Algorithm)
            return new MdnOutcome(true, null, null,
                $"the MIC in the MDN is computed with {received.Algorithm}, ours with {ours.Algorithm}, so they cannot be compared");

        if (!Mic.AreEqual(message.Mic, mdn.ReceivedContentMic))
        {
            var text = $"the MIC in the MDN ({mdn.ReceivedContentMic}) differs from ours ({message.Mic})";
            return message.Signed
                ? new MdnOutcome(false, TransferEventType.MicMismatch, text + ": the partner confirmed other content than we sent", null)
                : new MdnOutcome(true, null, null, text);
        }

        return new MdnOutcome(true, null, null, null);
    }

    /// <summary>Stores the MDN in the message and sets its state; returns the outcome.</summary>
    public static MdnOutcome Apply(OutgoingMessage message, Mdn mdn, DateTime now)
    {
        var outcome = Evaluate(message, mdn);
        message.MdnDisposition = Truncate(mdn.Disposition, 500);
        message.ReceivedMic = Truncate(mdn.ReceivedContentMic, 200);
        message.MdnText = Truncate(mdn.Text, 2000);
        message.MdnSigned = mdn.Signed;
        message.MdnMessageId = Truncate(mdn.MessageId, 250);
        message.DeliveredDate = now;
        message.Status = outcome.Delivered ? OutgoingStatus.Delivered : OutgoingStatus.NotDelivered;
        message.LastError = outcome.Delivered ? outcome.Warning : outcome.Problem;
        if (!outcome.Delivered)
            message.LastErrorDate = now;
        return outcome;
    }

    internal static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
