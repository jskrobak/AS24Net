using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

/// <summary>How the partner is asked to confirm a message.</summary>
public enum MdnMode
{
    /// <summary>No MDN is requested; a message is delivered once the partner accepted the HTTP request.</summary>
    None,

    /// <summary>The MDN comes back in the HTTP response of the message.</summary>
    Sync,

    /// <summary>The partner posts the MDN later to our URL (Receipt-Delivery-Option).</summary>
    Async,
}

/// <summary>
/// A remote AS2 station: its AS2 name and what our messages to it look like. The server it is reached at, the
/// security agreed with it and its certificates are those of its <see cref="Connection"/>, which several partners
/// differing only in the AS2 name may share.
/// </summary>
public class Partner
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>AS2 name of the partner (AS2-To of our messages, AS2-From of its ones).</summary>
    [Required]
    [StringLength(128)]
    public string As2Id { get; set; } = string.Empty;

    /// <summary>The server the partner is reached at, with the security settings and the certificates.</summary>
    public int ConnectionId { get; set; }
    public Connection Connection { get; set; } = null!;

    /// <summary>A disabled partner gets no messages and its messages are refused.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Identity messages are sent from when the queue item names none.</summary>
    public int? DefaultIdentityId { get; set; }
    public Identity? DefaultIdentity { get; set; }

    /// <summary>Media type of the payload when the queue item names none, e.g. application/edifact.</summary>
    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    [StringLength(200)]
    public string? Subject { get; set; }
}
