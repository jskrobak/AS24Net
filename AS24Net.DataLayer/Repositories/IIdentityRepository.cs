using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IIdentityRepository : IRepository<Identity, int>
{
    Task<DataFragment<Identity>> GetFragmentAsync(IdentityFilter filter, GridDataProviderRequest<Identity> request,
        CancellationToken cancellationToken = default);

    /// <summary>The identity with the AS2 name, with its certificates.</summary>
    Task<Identity?> FindByAs2IdAsync(string as2Id, CancellationToken cancellationToken = default);

    Task<Identity?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default);
}
