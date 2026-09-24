using Havit.Data.EntityFrameworkCore.Patterns.Repositories;
using Havit.Data.EntityFrameworkCore.Patterns.SoftDeletes;
using Microsoft.EntityFrameworkCore;
using AS24Net.Domain;
using DbContext = Havit.Data.EntityFrameworkCore.DbContext;

namespace AS24Net.DataLayer.Repositories;

public class ConnectionDbRepositoryQueryProvider: IRepositoryQueryProvider<Connection, int>
{
    private readonly ISoftDeleteManager _softDeleteManager;
        
    private readonly Func<DbContext, int, Connection> _getObjectQuery;
    private readonly Func<DbContext, int, CancellationToken, Task<Connection>> _getObjectAsyncQuery;
    private readonly Func<DbContext, int[], IEnumerable<Connection>> _getObjectsQuery;
    private readonly Func<DbContext, int[], IAsyncEnumerable<Connection>> _getObjectsAsyncQuery;
    private readonly Func<DbContext, IEnumerable<Connection>> _getAllQuery;
    private readonly Func<DbContext, IAsyncEnumerable<Connection>> _getAllAsyncQuery;


    public ConnectionDbRepositoryQueryProvider(ISoftDeleteManager softDeleteManager)
    {
        _softDeleteManager = softDeleteManager;

        _getObjectQuery = EF.CompileQuery((DbContext dbContext, int id) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetObject")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int id, CancellationToken cancellationToken) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetObjectAsync")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectsQuery = EF.CompileQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetObjects")
            .Where(entity => ids.Contains(entity.Id)));
       

        _getObjectsAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetObjectsAsync")
            .Where(entity => ids.Contains(entity.Id)));

        _getAllQuery = EF.CompileQuery((DbContext dbContext) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetAll")
            .WhereNotDeleted(_softDeleteManager));

        _getAllAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext) => dbContext
            .Set<Connection>()
            .TagWith("ConnectionDbRepository.GetAllAsync")
            .WhereNotDeleted(_softDeleteManager));
    }
    
    public Func<DbContext, int, Connection> GetGetObjectQuery() => _getObjectQuery;
    public Func<DbContext, int, CancellationToken, Task<Connection>> GetGetObjectAsyncQuery() => _getObjectAsyncQuery;
    public Func<DbContext, int[], IEnumerable<Connection>> GetGetObjectsQuery() => _getObjectsQuery;
    public Func<DbContext, int[], IAsyncEnumerable<Connection>> GetGetObjectsAsyncQuery() => _getObjectsAsyncQuery;
    public Func<DbContext, IAsyncEnumerable<Connection>> GetGetAllAsyncQuery() => _getAllAsyncQuery;
    public Func<DbContext, IEnumerable<Connection>> GetGetAllQuery() => _getAllQuery;

}