using Havit.Data.Patterns.Repositories;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IApiTokenRepository : IRepository<ApiToken, int>
{
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
}
