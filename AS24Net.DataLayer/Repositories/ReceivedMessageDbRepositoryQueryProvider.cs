using Havit.Data.EntityFrameworkCore.Patterns.Repositories;
using Havit.Data.EntityFrameworkCore.Patterns.SoftDeletes;
using Microsoft.EntityFrameworkCore;
using AS24Net.Domain;
using DbContext = Havit.Data.EntityFrameworkCore.DbContext;

namespace AS24Net.DataLayer.Repositories;

public class ReceivedMessageDbRepositoryQueryProvider: IRepositoryQueryProvider<ReceivedMessage, int>
{
    private readonly ISoftDeleteManager _softDeleteManager;
        
    private readonly Func<DbContext, int, ReceivedMessage> _getObjectQuery;
    private readonly Func<DbContext, int, CancellationToken, Task<ReceivedMessage>> _getObjectAsyncQuery;
    private readonly Func<DbContext, int[], IEnumerable<ReceivedMessage>> _getObjectsQuery;
    private readonly Func<DbContext, int[], IAsyncEnumerable<ReceivedMessage>> _getObjectsAsyncQuery;
    private readonly Func<DbContext, IEnumerable<ReceivedMessage>> _getAllQuery;
    private readonly Func<DbContext, IAsyncEnumerable<ReceivedMessage>> _getAllAsyncQuery;


    public ReceivedMessageDbRepositoryQueryProvider(ISoftDeleteManager softDeleteManager)
    {
        _softDeleteManager = softDeleteManager;

        _getObjectQuery = EF.CompileQuery((DbContext dbContext, int id) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetObject")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int id, CancellationToken cancellationToken) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetObjectAsync")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectsQuery = EF.CompileQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetObjects")
            .Where(entity => ids.Contains(entity.Id)));
       

        _getObjectsAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetObjectsAsync")
            .Where(entity => ids.Contains(entity.Id)));

        _getAllQuery = EF.CompileQuery((DbContext dbContext) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetAll")
            .WhereNotDeleted(_softDeleteManager));

        _getAllAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext) => dbContext
            .Set<ReceivedMessage>()
            .TagWith("ReceivedMessageDbRepository.GetAllAsync")
            .WhereNotDeleted(_softDeleteManager));
    }
    
    public Func<DbContext, int, ReceivedMessage> GetGetObjectQuery() => _getObjectQuery;
    public Func<DbContext, int, CancellationToken, Task<ReceivedMessage>> GetGetObjectAsyncQuery() => _getObjectAsyncQuery;
    public Func<DbContext, int[], IEnumerable<ReceivedMessage>> GetGetObjectsQuery() => _getObjectsQuery;
    public Func<DbContext, int[], IAsyncEnumerable<ReceivedMessage>> GetGetObjectsAsyncQuery() => _getObjectsAsyncQuery;
    public Func<DbContext, IAsyncEnumerable<ReceivedMessage>> GetGetAllAsyncQuery() => _getAllAsyncQuery;
    public Func<DbContext, IEnumerable<ReceivedMessage>> GetGetAllQuery() => _getAllQuery;

}