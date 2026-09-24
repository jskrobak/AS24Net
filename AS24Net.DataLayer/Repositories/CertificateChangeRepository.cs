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

public class CertificateChangeRepository(
    IDbContext dbContext,
    IEntityKeyAccessor<CertificateChange, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<CertificateChange, int> repositoryQueryProvider)
    : DbRepository<CertificateChange, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), ICertificateChangeRepository
{
    public async Task<DataFragment<CertificateChange>> GetFragmentAsync(CertificateChangeFilter filter, GridDataProviderRequest<CertificateChange> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data
            .Include(c => c.Certificate));

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<CertificateChange>(request).ToListAsync(cancellationToken);

        return new DataFragment<CertificateChange>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public Task<List<CertificateChange>> GetDueAsync(DateTime now, CancellationToken cancellationToken = default) => Data
        .Where(c => c.Status == CertificateChangeStatus.Scheduled && c.ActivateAt <= now)
        .Include(c => c.Connection).ThenInclude(c => c!.Partners)
        .Include(c => c.Certificate)
        .OrderBy(c => c.ActivateAt)
        .ThenBy(c => c.Id)
        .ToListAsync(cancellationToken);

    public Task<List<CertificateChange>> GetByConnectionAsync(int connectionId, CancellationToken cancellationToken = default) => Data
        .Where(c => c.ConnectionId == connectionId)
        .Include(c => c.Certificate)
        .OrderByDescending(c => c.ActivateAt)
        .ToListAsync(cancellationToken);

    public Task<DateTime?> GetNextScheduledAsync(CancellationToken cancellationToken = default) => Data
        .Where(c => c.Status == CertificateChangeStatus.Scheduled)
        .MinAsync(c => (DateTime?)c.ActivateAt, cancellationToken);

    public Task<List<CertificateChange>> GetScheduledAsync(CancellationToken cancellationToken = default) => Data
        .Where(c => c.Status == CertificateChangeStatus.Scheduled)
        .Include(c => c.Connection)
        .Include(c => c.Certificate)
        .OrderBy(c => c.ActivateAt)
        .ToListAsync(cancellationToken);
}
