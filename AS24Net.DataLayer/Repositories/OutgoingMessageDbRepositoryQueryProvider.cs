using Havit.Data.EntityFrameworkCore.Patterns.Repositories;
using Havit.Data.EntityFrameworkCore.Patterns.SoftDeletes;
using Microsoft.EntityFrameworkCore;
using AS24Net.Domain;
using DbContext = Havit.Data.EntityFrameworkCore.DbContext;

namespace AS24Net.DataLayer.Repositories;

public class OutgoingMessageDbRepositoryQueryProvider: IRepositoryQueryProvider<OutgoingMessage, int>
{
    private readonly ISoftDeleteManager _softDeleteManager;
        
    private readonly Func<DbContext, int, OutgoingMessage> _getObjectQuery;
    private readonly Func<DbContext, int, CancellationToken, Task<OutgoingMessage>> _getObjectAsyncQuery;
    private readonly Func<DbContext, int[], IEnumerable<OutgoingMessage>> _getObjectsQuery;
    private readonly Func<DbContext, int[], IAsyncEnumerable<OutgoingMessage>> _getObjectsAsyncQuery;
    private readonly Func<DbContext, IEnumerable<OutgoingMessage>> _getAllQuery;
    private readonly Func<DbContext, IAsyncEnumerable<OutgoingMessage>> _getAllAsyncQuery;


    public OutgoingMessageDbRepositoryQueryProvider(ISoftDeleteManager softDeleteManager)
    {
        _softDeleteManager = softDeleteManager;

        _getObjectQuery = EF.CompileQuery((DbContext dbContext, int id) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetObject")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int id, CancellationToken cancellationToken) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetObjectAsync")
            .Where(entity => entity.Id == id)
            .FirstOrDefault())!;

        _getObjectsQuery = EF.CompileQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetObjects")
            .Where(entity => ids.Contains(entity.Id)));
       

        _getObjectsAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext, int[] ids) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetObjectsAsync")
            .Where(entity => ids.Contains(entity.Id)));

        _getAllQuery = EF.CompileQuery((DbContext dbContext) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetAll")
            .WhereNotDeleted(_softDeleteManager));

        _getAllAsyncQuery = EF.CompileAsyncQuery((DbContext dbContext) => dbContext
            .Set<OutgoingMessage>()
            .TagWith("OutgoingMessageDbRepository.GetAllAsync")
            .WhereNotDeleted(_softDeleteManager));
    }
    
    public Func<DbContext, int, OutgoingMessage> GetGetObjectQuery() => _getObjectQuery;
    public Func<DbContext, int, CancellationToken, Task<OutgoingMessage>> GetGetObjectAsyncQuery() => _getObjectAsyncQuery;
    public Func<DbContext, int[], IEnumerable<OutgoingMessage>> GetGetObjectsQuery() => _getObjectsQuery;
    public Func<DbContext, int[], IAsyncEnumerable<OutgoingMessage>> GetGetObjectsAsyncQuery() => _getObjectsAsyncQuery;
    public Func<DbContext, IAsyncEnumerable<OutgoingMessage>> GetGetAllAsyncQuery() => _getAllAsyncQuery;
    public Func<DbContext, IEnumerable<OutgoingMessage>> GetGetAllQuery() => _getAllQuery;

}