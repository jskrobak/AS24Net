using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface ICertificateChangeRepository : IRepository<CertificateChange, int>
{
    Task<DataFragment<CertificateChange>> GetFragmentAsync(CertificateChangeFilter filter, GridDataProviderRequest<CertificateChange> request,
        CancellationToken cancellationToken = default);

    /// <summary>Scheduled changes whose time has come, the earliest first.</summary>
    Task<List<CertificateChange>> GetDueAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>When the next scheduled change is due, <c>null</c> when none is scheduled.</summary>
    Task<DateTime?> GetNextScheduledAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes still waiting for their time, with partner and certificate.</summary>
    Task<List<CertificateChange>> GetScheduledAsync(CancellationToken cancellationToken = default);
}
