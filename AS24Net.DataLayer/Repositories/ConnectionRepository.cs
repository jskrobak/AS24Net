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

public class ConnectionRepository(
    IDbContext dbContext,
    IEntityKeyAccessor<Connection, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<Connection, int> repositoryQueryProvider)
    : DbRepository<Connection, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), IConnectionRepository
{
    public async Task<DataFragment<Connection>> GetFragmentAsync(ConnectionFilter filter, GridDataProviderRequest<Connection> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data.Include(c => c.Partners));

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<Connection>(request).ToListAsync(cancellationToken);

        return new DataFragment<Connection>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public Task<Connection?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var value = name.Trim();
        return Data.FirstOrDefaultAsync(c => c.Name == value, cancellationToken);
    }

    public Task<List<Connection>> GetAllWithPartnersAsync(CancellationToken cancellationToken = default) => Data
        .Include(c => c.Partners)
        .OrderBy(c => c.Name)
        .ToListAsync(cancellationToken);
}
