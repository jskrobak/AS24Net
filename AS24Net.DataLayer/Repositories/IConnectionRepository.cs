using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IConnectionRepository : IRepository<Connection, int>
{
    Task<DataFragment<Connection>> GetFragmentAsync(ConnectionFilter filter, GridDataProviderRequest<Connection> request,
        CancellationToken cancellationToken = default);

    /// <summary>The connection with the name.</summary>
    Task<Connection?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The connections with their partners, by name.</summary>
    Task<List<Connection>> GetAllWithPartnersAsync(CancellationToken cancellationToken = default);
}
