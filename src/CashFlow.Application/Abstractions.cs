using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;

namespace CashFlow.Application;

/// <summary>Единица работы над всей моделью. Инфраструктура реализует поверх EF Core.</summary>
public interface IUnitOfWork
{
    IRepository<FinancialProfile> Profiles { get; }
    IRepository<Institution> Institutions { get; }
    IRepository<Connection> Connections { get; }
    IRepository<SyncRun> SyncRuns { get; }
    IRepository<RawRecord> RawRecords { get; }
    IRepository<Account> Accounts { get; }
    IRepository<BalanceSnapshot> BalanceSnapshots { get; }
    IRepository<Transaction> Transactions { get; }
    IRepository<TransferLink> TransferLinks { get; }
    IRepository<Counterparty> Counterparties { get; }
    IRepository<Category> Categories { get; }
    IRepository<CategorizationRule> Rules { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    Task<T?> FindAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    void Remove(T entity);
}

/// <summary>
/// Хранилище секретов (токены, сертификаты). Значения шифруются ключом пользователя.
/// Домен видит только CredentialRef.
/// </summary>
public interface ISecretStore
{
    Task<string> PutAsync(string userId, IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAsync(string userId, string credentialRef, CancellationToken ct = default);
    Task DeleteAsync(string userId, string credentialRef, CancellationToken ct = default);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
