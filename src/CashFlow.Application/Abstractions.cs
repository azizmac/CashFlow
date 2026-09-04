using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;

namespace CashFlow.Application;

// Инфраструктурные контракты: реализуются в CashFlow.Infrastructure. Контракты сервисов для UI — в Abstractions/Services.cs.

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
/// Хранилище секретов (токены, сертификаты). Значения шифруются ключом сервера.
/// Домен видит только CredentialRef, наружу (DTO) не уходит даже он.
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

/// <summary>Часовой пояс для отображения и группировки по датам (CASHFLOW_TZ, по умолчанию Москва).</summary>
public static class AppTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        var id = Environment.GetEnvironmentVariable("CASHFLOW_TZ") ?? "Europe/Moscow";
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "Moscow", "Moscow"); }
    }

    public static DateTimeOffset ToLocal(this DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);
    public static DateOnly LocalDate(this DateTimeOffset utc) => DateOnly.FromDateTime(ToLocal(utc).DateTime);
}
