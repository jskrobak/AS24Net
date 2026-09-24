namespace AS24Net.Core.As2;

/// <summary>
/// Error modifiers of the disposition of an MDN (RFC 4130 7.4.3, RFC 5402): the reason a message was not processed,
/// as the partner reads it in <c>Disposition: automatic-action/MDN-sent-automatically; processed/error: …</c>.
/// </summary>
public static class As2Errors
{
    public const string DecryptionFailed = "decryption-failed";
    public const string AuthenticationFailed = "authentication-failed";
    public const string IntegrityCheckFailed = "integrity-check-failed";
    public const string DecompressionFailed = "decompression-failed";
    public const string InsufficientMessageSecurity = "insufficient-message-security";
    public const string UnexpectedProcessingError = "unexpected-processing-error";

    /// <summary>The AS2-From / AS2-To pair is not known here (a failure, the message is not processed).</summary>
    public const string UnknownTradingPartner = "unknown-trading-partner";
}

/// <summary>
/// A received message or MDN cannot be processed. <see cref="Error"/> is the modifier the partner is told in the
/// MDN (see <see cref="As2Errors"/>).
/// </summary>
public class As2ProcessingException(string error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Error { get; } = error;
}
