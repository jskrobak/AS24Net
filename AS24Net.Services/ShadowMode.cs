using Microsoft.Extensions.Configuration;

namespace AS24Net.Services;

/// <summary>
/// Shadow mode (configuration <c>Shadow:Enabled</c>): the server receives a copy of the production traffic, e.g.
/// from a mirror on the reverse proxy, and processes it fully, but nothing leaves it. Asynchronous MDNs are built
/// and not posted, nothing can be queued or sent, hooks and webhooks do not run and connection tests do not contact
/// partners, so that partners never see the server and the systems behind production never get anything twice.
/// </summary>
public sealed class ShadowMode(IConfiguration configuration)
{
    public const string ConfigurationKey = "Shadow:Enabled";

    public bool Enabled { get; } = IsEnabled(configuration);

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue(ConfigurationKey, false);

    /// <summary>Why something is not done, for the messages and the log.</summary>
    public const string Reason = "The server runs in shadow mode (Shadow:Enabled): it does not send anything to partners.";
}
