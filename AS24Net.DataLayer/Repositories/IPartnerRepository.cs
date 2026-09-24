using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IPartnerRepository : IRepository<Partner, int>
{
    Task<DataFragment<Partner>> GetFragmentAsync(PartnerFilter filter, GridDataProviderRequest<Partner> request,
        CancellationToken cancellationToken = default);

    /// <summary>The partner with the AS2 name, with its certificates.</summary>
    Task<Partner?> FindByAs2IdAsync(string as2Id, CancellationToken cancellationToken = default);

    /// <summary>The partner with its certificates and default identity.</summary>
    Task<Partner?> FindWithRefsAsync(int id, CancellationToken cancellationToken = default);
}
