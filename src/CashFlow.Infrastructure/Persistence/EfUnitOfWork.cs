using CashFlow.Application;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Infrastructure.Persistence;

public sealed class EfRepository<T> : IRepository<T> where T : class
{
    private readonly DbSet<T> _set;
    public EfRepository(CashFlowDbContext db) => _set = db.Set<T>();

    public IQueryable<T> Query() => _set;
    public async Task<T?> FindAsync(Guid id, CancellationToken ct = default) => await _set.FindAsync([id], ct);
    public async Task AddAsync(T entity, CancellationToken ct = default) => await _set.AddAsync(entity, ct);
    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => await _set.AddRangeAsync(entities, ct);
    public void Remove(T entity) => _set.Remove(entity);
}

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly CashFlowDbContext _db;

    public EfUnitOfWork(CashFlowDbContext db)
    {
        _db = db;
        Profiles = new EfRepository<FinancialProfile>(db);
        Institutions = new EfRepository<Institution>(db);
        Connections = new EfRepository<Connection>(db);
        SyncRuns = new EfRepository<SyncRun>(db);
        RawRecords = new EfRepository<RawRecord>(db);
        Accounts = new EfRepository<Account>(db);
        BalanceSnapshots = new EfRepository<BalanceSnapshot>(db);
        Transactions = new EfRepository<Transaction>(db);
        TransferLinks = new EfRepository<TransferLink>(db);
        Counterparties = new EfRepository<Counterparty>(db);
        Categories = new EfRepository<Category>(db);
        Rules = new EfRepository<CategorizationRule>(db);
    }

    public IRepository<FinancialProfile> Profiles { get; }
    public IRepository<Institution> Institutions { get; }
    public IRepository<Connection> Connections { get; }
    public IRepository<SyncRun> SyncRuns { get; }
    public IRepository<RawRecord> RawRecords { get; }
    public IRepository<Account> Accounts { get; }
    public IRepository<BalanceSnapshot> BalanceSnapshots { get; }
    public IRepository<Transaction> Transactions { get; }
    public IRepository<TransferLink> TransferLinks { get; }
    public IRepository<Counterparty> Counterparties { get; }
    public IRepository<Category> Categories { get; }
    public IRepository<CategorizationRule> Rules { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
