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

public class CertificateRepository(
    IDbContext dbContext,
    IEntityKeyAccessor<Certificate, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<Certificate, int> repositoryQueryProvider)
    : DbRepository<Certificate, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), ICertificateRepository
{
    public async Task<DataFragment<Certificate>> GetFragmentAsync(CertificateFilter filter, GridDataProviderRequest<Certificate> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data);

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<Certificate>(request).ToListAsync(cancellationToken);

        return new DataFragment<Certificate>
        {
            Data = data,
            TotalCount = cnt
        };
    }
}
