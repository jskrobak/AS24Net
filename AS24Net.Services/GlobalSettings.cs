using System.ComponentModel.DataAnnotations;

namespace AS24Net.Services;

/// <summary>Settings of the server kept in the database and edited on the settings page.</summary>
public class GlobalSettings
{
    /// <summary>
    /// Public address of our AS2 endpoint (e.g. <c>https://as2.example.com/as2</c>), given to partners and sent as
    /// the address of asynchronous MDNs (Receipt-Delivery-Option).
    /// </summary>
    [SettingsItem]
    [Url]
    public string? PublicUrl { get; set; }

    /// <summary>Directory where received payloads are stored, in a sub-directory per partner.</summary>
    [SettingsItem]
    [Required]
    public string ReceiveDirectory { get; set; } = "received";

    /// <summary>Directory where payloads put into the send queue are stored.</summary>
    [SettingsItem]
    [Required]
    public string OutboxDirectory { get; set; } = "outbox";

    /// <summary>How often the send queue is checked.</summary>
    [SettingsItem]
    [Range(5, 86400)]
    public int SendIntervalSeconds { get; set; } = 30;

    /// <summary>Messages are sent at the same time up to this number (to different partners or to the same one).</summary>
    [SettingsItem]
    [Range(1, 100)]
    public int MaxParallelSends { get; set; } = 8;

    /// <summary>Failed transfers are retried up to this number of times, then marked as failed.</summary>
    [SettingsItem]
    [Range(0, 1000)]
    public int MaxRetryCount { get; set; } = 10;

    /// <summary>
    /// Delay before the first retry; every further retry waits twice as long, up to <see cref="MaxRetryDelayMinutes"/>.
    /// </summary>
    [SettingsItem]
    [Range(1, 1440)]
    public int RetryDelayMinutes { get; set; } = 1;

    [SettingsItem]
    [Range(1, 10080)]
    public int MaxRetryDelayMinutes { get; set; } = 60;

    /// <summary>Asynchronous MDNs that cannot be posted are retried up to this number of times.</summary>
    [SettingsItem]
    [Range(0, 1000)]
    public int MaxMdnRetryCount { get; set; } = 10;

    /// <summary>Messages larger than this are refused (HTTP 413); they are processed in memory.</summary>
    [SettingsItem]
    [Range(1, 4096)]
    public int MaxMessageSizeMb { get; set; } = 100;

    /// <summary>Transfer log records older than this are archived (hidden unless the complete archive is shown).</summary>
    [SettingsItem]
    [Range(1, 3650)]
    public int ArchiveEventsAfterDays { get; set; } = 90;

    /// <summary>
    /// Old data is removed every night (see <see cref="Retention.RetentionService"/>); what is removed from the
    /// database is written to <see cref="RetentionArchiveDirectory"/> first.
    /// </summary>
    [SettingsItem]
    public bool RetentionEnabled { get; set; } = true;

    /// <summary>
    /// The details of transfer log records (messages of exceptions, output of hooks, bodies of webhooks) and the
    /// parameters of hook runs are removed, and the payloads of delivered messages are deleted from the outbox.
    /// </summary>
    [SettingsItem]
    [Range(7, 36500)]
    public int DeleteContentAfterDays { get; set; } = 30;

    /// <summary>Informational transfer log records are deleted (at the earliest with the content).</summary>
    [SettingsItem]
    [Range(7, 36500)]
    public int DeleteInformationEventsAfterDays { get; set; } = 90;

    /// <summary>Warnings and errors of the transfer log are deleted (at the earliest with the informational ones).</summary>
    [SettingsItem]
    [Range(7, 36500)]
    public int DeleteEventsAfterDays { get; set; } = 365;

    /// <summary>
    /// Sent and received messages that need nothing more (the MDN was exchanged) are deleted from the database.
    /// At least 30 days, because a message received again within that time is recognised as a duplicate by its
    /// record. The received payloads themselves stay.
    /// </summary>
    [SettingsItem]
    [Range(30, 36500)]
    public int DeleteFinishedMessagesAfterDays { get; set; } = 365;

    /// <summary>Directory the removed data is written to, as compressed JSON lines per month.</summary>
    [SettingsItem]
    [Required]
    public string RetentionArchiveDirectory { get; set; } = "archive";
}
