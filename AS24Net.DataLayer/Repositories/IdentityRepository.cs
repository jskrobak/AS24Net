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

public class IdentityRepository(
    IDbContext dbContext,
    IEntityKeyAccessor<Identity, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<Identity, int> repositoryQueryProvider)
    : DbRepository<Identity, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), IIdentityRepository
{
    public async Task<DataFragment<Identity>> GetFragmentAsync(IdentityFilter filter, GridDataProviderRequest<Identity> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data);

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<Identity>(request).ToListAsync(cancellationToken);

        return new DataFragment<Identity>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public Task<Identity?> FindByAs2IdAsync(string as2Id, CancellationToken cancellationToken = default)
    {
        var value = as2Id.Trim();
        return WithRefs().FirstOrDefaultAsync(i => i.As2Id == value, cancellationToken);
    }

    public Task<Identity?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default) =>
        WithRefs().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    private IQueryable<Identity> WithRefs() => Data
        .Include(i => i.SigningCertificate)
        .Include(i => i.DecryptionCertificate)
        .Include(i => i.PreviousDecryptionCertificate);
}
