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

public class PartnerRepository(
    IDbContext dbContext,
    IEntityKeyAccessor<Partner, int> entityKeyAccessor,
    IDataLoader dataLoader,
    ISoftDeleteManager softDeleteManager,
    IEntityCacheManager entityCacheManager,
    IRepositoryQueryProvider<Partner, int> repositoryQueryProvider)
    : DbRepository<Partner, int>(dbContext, entityKeyAccessor, dataLoader, softDeleteManager, entityCacheManager,
        repositoryQueryProvider), IPartnerRepository
{
    public async Task<DataFragment<Partner>> GetFragmentAsync(PartnerFilter filter, GridDataProviderRequest<Partner> request,
        CancellationToken cancellationToken = default)
    {
        var filtered = filter.Apply(Data.Include(p => p.Connection));

        var cnt = await filtered.CountAsync(cancellationToken);

        var data = await filtered.ApplyGridDataProviderRequest<Partner>(request).ToListAsync(cancellationToken);

        return new DataFragment<Partner>
        {
            Data = data,
            TotalCount = cnt
        };
    }

    public Task<Partner?> FindByAs2IdAsync(string as2Id, CancellationToken cancellationToken = default)
    {
        var value = as2Id.Trim();
        return WithRefs().FirstOrDefaultAsync(p => p.As2Id == value, cancellationToken);
    }

    public Task<Partner?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default) =>
        WithRefs().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<List<Partner>> GetAllWithConnectionAsync(CancellationToken cancellationToken = default) => Data
        .Include(p => p.Connection)
        .Include(p => p.DefaultIdentity)
        .OrderBy(p => p.Name)
        .ToListAsync(cancellationToken);

    private IQueryable<Partner> WithRefs() => Data
        .Include(p => p.Connection).ThenInclude(c => c.SignatureCertificate)
        .Include(p => p.Connection).ThenInclude(c => c.PreviousSignatureCertificate)
        .Include(p => p.Connection).ThenInclude(c => c.EncryptionCertificate)
        .Include(p => p.Connection).ThenInclude(c => c.TlsCertificate)
        .Include(p => p.DefaultIdentity);
}
