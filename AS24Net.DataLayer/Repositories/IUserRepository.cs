using Havit.Data.Patterns.Repositories;
using AS24Net.Domain;

namespace AS24Net.DataLayer.Repositories;

public interface IUserRepository : IRepository<User, int>
{
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>The user who signs in with this e-mail address or user principal name through Entra ID.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
