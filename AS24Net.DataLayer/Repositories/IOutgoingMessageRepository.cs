using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

/// <summary>The send queue of one partner in numbers (<c>/health/send-queue</c>).</summary>
public sealed record SendQueuePartnerState(int PartnerId, int Waiting, DateTime? OldestWaiting, int Failed,
    int AwaitingMdn, DateTime? OldestSent, string? LastError, DateTime? LastErrorDate);

public interface IOutgoingMessageRepository : IRepository<OutgoingMessage, int>
{
    Task<DataFragment<OutgoingMessage>> GetFragmentAsync(OutgoingMessageFilter filter, GridDataProviderRequest<OutgoingMessage> request,
        CancellationToken cancellationToken = default);

    /// <summary>Messages new or to be retried whose time has come, with partner and identity; the oldest first.</summary>
    Task<List<OutgoingMessage>> GetAllToProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>Filtered page of messages, newest first (REST API).</summary>
    Task<DataFragment<OutgoingMessage>> GetListAsync(OutgoingMessageFilter filter, int skip, int take,
        CancellationToken cancellationToken = default);

    Task<OutgoingMessage?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The message an MDN belongs to.</summary>
    Task<OutgoingMessage?> FindByMessageIdAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>Messages sent that wait for an asynchronous MDN, with their partner.</summary>
    Task<List<OutgoingMessage>> GetAwaitingMdnAsync(CancellationToken cancellationToken = default);

    /// <summary>Messages still waiting to be sent (new or to be retried) that were queued before the given time.</summary>
    Task<int> CountWaitingAsync(DateTime createdBefore, CancellationToken cancellationToken = default);

    /// <summary>Messages that failed for good or were not delivered since the given time.</summary>
    Task<int> CountFailedAsync(DateTime failedSince, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per partner with anything in the queue: messages waiting (new or to be retried), failed for good or not
    /// delivered, and sent awaiting their asynchronous MDN, with the oldest of them and the last error.
    /// </summary>
    Task<List<SendQueuePartnerState>> GetStateByPartnerAsync(CancellationToken cancellationToken = default);

    /// <summary>Messages that need nothing more, queued before the given time, with partner and identity; the oldest first.</summary>
    Task<List<OutgoingMessage>> GetFinishedAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Of the given files, those that belong only to messages delivered before the given time; a file of no
    /// message is not returned.
    /// </summary>
    Task<List<string>> GetFilesDeliveredBeforeAsync(IReadOnlyCollection<string> filePaths, DateTime deliveredBefore,
        CancellationToken cancellationToken = default);
}
