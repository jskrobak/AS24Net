namespace AS24Net.Domain;

/// <summary>
/// What an API token may do besides reading: partners, identities, connections, the transfer log, the status and the
/// health checks are open to every token.
/// </summary>
[Flags]
public enum ApiTokenScopes
{
    None = 0,

    /// <summary>Putting messages into the send queue, reading, retrying and removing them.</summary>
    Messages = 1,

    /// <summary>Fetching received messages; the inbox webhook of the token is called only with this scope.</summary>
    Inbox = 2,

    /// <summary>
    /// Scheduling and cancelling certificate changes of connections, listing the stored certificates and testing the
    /// connection to a partner. Nothing else of a connection can be changed.
    /// </summary>
    Certificates = 4,

    /// <summary>Setting up connections, partners, identities and certificates, e.g. for an import; includes <see cref="Certificates"/>.</summary>
    Configuration = 8,

    /// <summary>What every token could do before tokens had scopes, apart from <see cref="Configuration"/>.</summary>
    Default = Messages | Inbox | Certificates,
}

public static class ApiTokenScopesExtensions
{
    /// <summary>The scopes a token effectively has: <see cref="ApiTokenScopes.Configuration"/> includes <see cref="ApiTokenScopes.Certificates"/>.</summary>
    public static ApiTokenScopes Effective(this ApiTokenScopes scopes) =>
        scopes.HasFlag(ApiTokenScopes.Configuration) ? scopes | ApiTokenScopes.Certificates : scopes;

    /// <summary>The individual scopes, without <see cref="ApiTokenScopes.None"/> and <see cref="ApiTokenScopes.Default"/>.</summary>
    public static IEnumerable<ApiTokenScopes> Each(this ApiTokenScopes scopes) =>
        All.Where(scope => scopes.HasFlag(scope));

    public static readonly IReadOnlyList<ApiTokenScopes> All =
        [ApiTokenScopes.Messages, ApiTokenScopes.Inbox, ApiTokenScopes.Certificates, ApiTokenScopes.Configuration];
}
