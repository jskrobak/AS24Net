using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IReceivedMessageRepository : IRepository<ReceivedMessage, int>
{
    Task<DataFragment<ReceivedMessage>> GetFragmentAsync(ReceivedMessageFilter filter, GridDataProviderRequest<ReceivedMessage> request,
        CancellationToken cancellationToken = default);

    /// <summary>Filtered page of messages, newest first (REST API).</summary>
    Task<DataFragment<ReceivedMessage>> GetListAsync(ReceivedMessageFilter filter, int skip, int take,
        CancellationToken cancellationToken = default);

    Task<ReceivedMessage?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>A message of the partner with the Message-ID that was received and stored before.</summary>
    Task<ReceivedMessage?> FindReceivedAsync(string as2From, string messageId, CancellationToken cancellationToken = default);

    /// <summary>Messages whose asynchronous MDN is due to be posted, with partner and identity.</summary>
    Task<List<ReceivedMessage>> GetMdnsToSendAsync(CancellationToken cancellationToken = default);

    /// <summary>Asynchronous MDNs still not posted for messages received before the given time.</summary>
    Task<int> CountMdnsPendingAsync(DateTime receivedBefore, CancellationToken cancellationToken = default);

    /// <summary>Messages refused since the given time.</summary>
    Task<int> CountFailedAsync(DateTime since, CancellationToken cancellationToken = default);

    /// <summary>Messages that need nothing more (their MDN is sent), received before the given time; the oldest first.</summary>
    Task<List<ReceivedMessage>> GetFinishedAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}
