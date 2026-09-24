using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.EntityFrameworkCore;
using Havit.Data.EntityFrameworkCore.Patterns.Caching;
using Havit.Data.EntityFrameworkCore.Patterns.Repositories;
using Havit.Data.EntityFrameworkCore.Patterns.SoftDeletes;
using Havit.Data.Patterns.DataLoaders;
using Havit.Data.Patterns.Infrastructure;
using Microsoft.EntityFrameworkCore;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;
using Havit.Services.TimeServices;

public class OutgoingMessageRepository(
    ITimeService timeService,
    IDbContext dbContext,
    IEntityKeyAccessor<OutgoingMessage, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<OutgoingMessage, int> repositoryQueryProvider)
    : DbRepository<OutgoingMessage, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), IOutgoingMessageRepository
{
    public async Task<DataFragment<OutgoingMessage>> GetFragmentAsync(OutgoingMessageFilter filter, GridDataProviderRequest<OutgoingMessage> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data
            .Include(m => m.Partner).ThenInclude(p => p.Connection)
            .Include(m => m.Identity));

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<OutgoingMessage>(request).ToListAsync(cancellationToken);

        return new DataFragment<OutgoingMessage>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public Task<List<OutgoingMessage>> GetAllToProcessAsync(CancellationToken cancellationToken = default)
    {
        var now = timeService.GetCurrentTime();
        return Data
            .Where(m => (m.Status == OutgoingStatus.New || m.Status == OutgoingStatus.Error) && m.NextRetry <= now)
            .Include(m => m.Partner).ThenInclude(p => p.Connection)
            .Include(m => m.Identity)
            .OrderBy(m => m.NextRetry)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<DataFragment<OutgoingMessage>> GetListAsync(OutgoingMessageFilter filter, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data);
        var count = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(m => m.Id)
            .Skip(skip)
            .Take(take)
            .Include(m => m.Partner).ThenInclude(p => p.Connection)
            .Include(m => m.Identity)
            .ToListAsync(cancellationToken);

        return new DataFragment<OutgoingMessage> { Data = items, TotalCount = count };
    }

    public Task<OutgoingMessage?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default) => Data
        .Include(m => m.Partner).ThenInclude(p => p.Connection)
        .Include(m => m.Identity)
        .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<OutgoingMessage?> FindByMessageIdAsync(string messageId, CancellationToken cancellationToken = default) => Data
        .Include(m => m.Partner).ThenInclude(p => p.Connection)
        .Include(m => m.Identity)
        .FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

    public Task<List<OutgoingMessage>> GetAwaitingMdnAsync(CancellationToken cancellationToken = default) => Data
        .Where(m => m.Status == OutgoingStatus.Sent)
        .Include(m => m.Partner).ThenInclude(p => p.Connection)
        .ToListAsync(cancellationToken);

    public Task<int> CountWaitingAsync(DateTime createdBefore, CancellationToken cancellationToken = default) => Data
        .Where(m => (m.Status == OutgoingStatus.New || m.Status == OutgoingStatus.Error) && m.Created < createdBefore)
        .CountAsync(cancellationToken);

    public Task<int> CountFailedAsync(DateTime failedSince, CancellationToken cancellationToken = default) => Data
        .Where(m => (m.Status == OutgoingStatus.Failed && m.LastErrorDate >= failedSince)
                    || (m.Status == OutgoingStatus.NotDelivered && m.DeliveredDate >= failedSince))
        .CountAsync(cancellationToken);

    public async Task<List<SendQueuePartnerState>> GetStateByPartnerAsync(CancellationToken cancellationToken = default)
    {
        var counts = await Data
            .Where(m => m.Status == OutgoingStatus.New || m.Status == OutgoingStatus.Error || m.Status == OutgoingStatus.Failed
                        || m.Status == OutgoingStatus.NotDelivered || m.Status == OutgoingStatus.Sent)
            .GroupBy(m => m.PartnerId)
            .Select(g => new
            {
                PartnerId = g.Key,
                Waiting = g.Count(m => m.Status == OutgoingStatus.New || m.Status == OutgoingStatus.Error),
                OldestWaiting = g.Where(m => m.Status == OutgoingStatus.New || m.Status == OutgoingStatus.Error).Min(m => (DateTime?)m.Created),
                Failed = g.Count(m => m.Status == OutgoingStatus.Failed || m.Status == OutgoingStatus.NotDelivered),
                AwaitingMdn = g.Count(m => m.Status == OutgoingStatus.Sent),
                OldestSent = g.Where(m => m.Status == OutgoingStatus.Sent).Min(m => m.SentDate),
            })
            .ToListAsync(cancellationToken);

        // The error of the message that failed last, among those still waiting or failed for good.
        var errors = await Data
            .Where(m => (m.Status == OutgoingStatus.Error || m.Status == OutgoingStatus.Failed || m.Status == OutgoingStatus.NotDelivered)
                        && m.LastErrorDate != null)
            .GroupBy(m => m.PartnerId)
            .Select(g => g.OrderByDescending(m => m.LastErrorDate).Select(m => new { m.PartnerId, m.LastError, m.LastErrorDate }).First())
            .ToListAsync(cancellationToken);
        var errorByPartner = errors.ToDictionary(e => e.PartnerId);

        return counts
            .Select(c => errorByPartner.TryGetValue(c.PartnerId, out var error)
                ? new SendQueuePartnerState(c.PartnerId, c.Waiting, c.OldestWaiting, c.Failed, c.AwaitingMdn, c.OldestSent,
                    error.LastError, error.LastErrorDate)
                : new SendQueuePartnerState(c.PartnerId, c.Waiting, c.OldestWaiting, c.Failed, c.AwaitingMdn, c.OldestSent, null, null))
            .ToList();
    }

    public Task<List<OutgoingMessage>> GetFinishedAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default) => Data
        .AsNoTracking()
        .Where(m => (m.Status == OutgoingStatus.Delivered || m.Status == OutgoingStatus.NotDelivered) && m.Created < createdBefore)
        .Include(m => m.Partner).ThenInclude(p => p.Connection)
        .Include(m => m.Identity)
        .OrderBy(m => m.Id)
        .Take(take)
        .ToListAsync(cancellationToken);

    public Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        Data.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);

    public async Task<List<string>> GetFilesDeliveredBeforeAsync(IReadOnlyCollection<string> filePaths, DateTime deliveredBefore,
        CancellationToken cancellationToken = default)
    {
        var items = await Data
            .Where(m => filePaths.Contains(m.FilePath))
            .Select(m => new { m.FilePath, m.Status, m.DeliveredDate })
            .ToListAsync(cancellationToken);

        // A file sent again in another message stays until that one is delivered as well.
        return items
            .GroupBy(m => m.FilePath)
            .Where(g => g.All(m => m.Status == OutgoingStatus.Delivered && m.DeliveredDate < deliveredBefore))
            .Select(g => g.Key)
            .ToList();
    }
}
