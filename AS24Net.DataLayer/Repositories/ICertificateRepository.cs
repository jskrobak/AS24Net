using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface ICertificateRepository : IRepository<Certificate, int>
{
    Task<DataFragment<Certificate>> GetFragmentAsync(CertificateFilter filter, GridDataProviderRequest<Certificate> request,
        CancellationToken cancellationToken = default);
}
