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

public class ReceivedMessageRepository(
    ITimeService timeService,
    IDbContext dbContext,
    IEntityKeyAccessor<ReceivedMessage, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<ReceivedMessage, int> repositoryQueryProvider)
    : DbRepository<ReceivedMessage, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), IReceivedMessageRepository
{
    public async Task<DataFragment<ReceivedMessage>> GetFragmentAsync(ReceivedMessageFilter filter, GridDataProviderRequest<ReceivedMessage> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data
            .Include(m => m.Partner)
            .Include(m => m.Identity));

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<ReceivedMessage>(request).ToListAsync(cancellationToken);

        return new DataFragment<ReceivedMessage>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public async Task<DataFragment<ReceivedMessage>> GetListAsync(ReceivedMessageFilter filter, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data);
        var count = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(m => m.Id)
            .Skip(skip)
            .Take(take)
            .Include(m => m.Partner)
            .Include(m => m.Identity)
            .ToListAsync(cancellationToken);

        return new DataFragment<ReceivedMessage> { Data = items, TotalCount = count };
    }

    public Task<ReceivedMessage?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default) => Data
        .Include(m => m.Partner)
        .Include(m => m.Identity)
        .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<ReceivedMessage?> FindReceivedAsync(string as2From, string messageId, CancellationToken cancellationToken = default) => Data
        .Where(m => m.As2From == as2From && m.MessageId == messageId && m.Status == ReceivedStatus.Received)
        .OrderBy(m => m.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public Task<List<ReceivedMessage>> GetMdnsToSendAsync(CancellationToken cancellationToken = default)
    {
        var now = timeService.GetCurrentTime();
        return Data
            .Where(m => (m.MdnStatus == MdnDeliveryStatus.Pending || m.MdnStatus == MdnDeliveryStatus.Retrying)
                        && (m.MdnNextRetry == null || m.MdnNextRetry <= now))
            .Include(m => m.Partner)
            .Include(m => m.Identity)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountMdnsPendingAsync(DateTime receivedBefore, CancellationToken cancellationToken = default) => Data
        .Where(m => (m.MdnStatus == MdnDeliveryStatus.Pending || m.MdnStatus == MdnDeliveryStatus.Retrying
                     || m.MdnStatus == MdnDeliveryStatus.Failed) && m.Created < receivedBefore)
        .CountAsync(cancellationToken);

    public Task<int> CountFailedAsync(DateTime since, CancellationToken cancellationToken = default) => Data
        .Where(m => m.Status == ReceivedStatus.Failed && m.Created >= since)
        .CountAsync(cancellationToken);

    public Task<List<ReceivedMessage>> GetFinishedAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default) => Data
        .AsNoTracking()
        .Where(m => m.Created < createdBefore && m.MdnStatus != MdnDeliveryStatus.Pending && m.MdnStatus != MdnDeliveryStatus.Retrying)
        .Include(m => m.Partner)
        .OrderBy(m => m.Id)
        .Take(take)
        .ToListAsync(cancellationToken);

    public Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        Data.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
}
