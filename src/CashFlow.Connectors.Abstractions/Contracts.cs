using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Connectors.Abstractions;

[Flags]
public enum ConnectorCapabilities
{
    None = 0,
    Accounts = 1,
    Balances = 2,
    Transactions = 4,
    Positions = 8,
    Products = 16,
    Realtime = 32,
    /// <summary>Коннектор принимает файлы (выписки), а не ходит в API.</summary>
    FileImport = 64,
}

/// <summary>Контекст вызова: подключение + расшифрованные секреты (живут только в памяти на время вызова).</summary>
public sealed record ConnectionContext(Guid ConnectionId, string UserId, IReadOnlyDictionary<string, string> Secrets, string? SyncCursor)
{
    public string Secret(string key) => Secrets.TryGetValue(key, out var v) ? v : throw new InvalidOperationException($"Secret '{key}' is missing");

    /// <summary>Коннектор вызывает, если провайдер ротировал секреты (например, одноразовый refresh_token). Инфраструктура сохраняет.</summary>
    public Func<IReadOnlyDictionary<string, string>, CancellationToken, Task>? OnSecretsRotated { get; init; }
}

public sealed record ExternalAccount(
    string ExternalId,
    string Name,
    AccountType Type,
    Currency Currency,
    string? AccountNumber,
    Money? Balance,
    Money? Available = null,
    Money? Blocked = null);

public sealed record ExternalTransaction(
    string? ExternalId,
    string AccountExternalId,
    DateTimeOffset PostedAt,
    Money Amount,
    string Description,
    CounterpartyRaw Counterparty,
    string? Purpose = null,
    string? Mcc = null,
    TransactionStatus Status = TransactionStatus.Posted,
    DateTimeOffset? BookedAt = null,
    string? RawPayload = null);

public sealed record ExternalPosition(
    string AccountExternalId,
    string InstrumentExternalId,
    string Ticker,
    string? Isin,
    string Name,
    string InstrumentType,
    decimal Quantity,
    Money? AveragePrice,
    Money? CurrentPrice);

public sealed record ExternalProduct(
    string AccountExternalId,
    string ProductType,           // Deposit | Loan | CreditCard
    decimal? RatePercent,
    DateOnly? OpenedAt,
    DateOnly? MaturityAt,
    Money? Principal,
    Money? Debt,
    Money? CreditLimit);

/// <summary>Результат импорта одного файла выписки.</summary>
public sealed record StatementParseResult(
    ExternalAccount Account,
    IReadOnlyList<ExternalTransaction> Transactions,
    DateRange? Period,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Коннектор — только чтение. Методов записи здесь нет и не будет.
/// </summary>
public interface IConnector
{
    ConnectorType Type { get; }
    ConnectorCapabilities Capabilities { get; }
    /// <summary>Какие секреты нужны (ключи для ISecretStore), например ["token"].</summary>
    IReadOnlyList<string> RequiredSecrets { get; }

    Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct);
    Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct);
    Task<IReadOnlyList<ExternalPosition>> GetPositionsAsync(ConnectionContext ctx, string accountExternalId, CancellationToken ct);
    Task<IReadOnlyList<ExternalProduct>> GetProductsAsync(ConnectionContext ctx, CancellationToken ct);
}

/// <summary>Парсер файла выписки конкретного банка.</summary>
public interface IStatementParser
{
    string BankCode { get; }               // Institution.Codes
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    /// <summary>Быстрая проверка, похож ли файл на выписку этого банка.</summary>
    Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct);
    Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct);
}

public abstract class ReadOnlyConnectorBase : IConnector
{
    public abstract ConnectorType Type { get; }
    public abstract ConnectorCapabilities Capabilities { get; }
    public abstract IReadOnlyList<string> RequiredSecrets { get; }

    public virtual Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct) => NotSupported<ExternalAccount>();
    public virtual Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct) => NotSupported<ExternalTransaction>();
    public virtual Task<IReadOnlyList<ExternalPosition>> GetPositionsAsync(ConnectionContext ctx, string accountExternalId, CancellationToken ct) => NotSupported<ExternalPosition>();
    public virtual Task<IReadOnlyList<ExternalProduct>> GetProductsAsync(ConnectionContext ctx, CancellationToken ct) => NotSupported<ExternalProduct>();

    private static Task<IReadOnlyList<T>> NotSupported<T>() => Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
}
