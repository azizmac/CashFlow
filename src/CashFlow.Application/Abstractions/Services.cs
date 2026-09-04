using CashFlow.Application.Contracts;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application;

// Контракты прикладного слоя. Это единственная граница между UI (Blazor, MAUI) и бэкендом:
// на сервере они реализованы поверх EF Core, в MAUI — поверх HTTP-клиента к CashFlow.Api. UI об этом не знает.
// userId всегда передаётся явно: API берёт его из токена, веб — из cookie, клиент MAUI игнорирует (сервер подставит свой).

public interface IProfileService
{
    Task<IReadOnlyList<ProfileDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<ProfileDto> CreateAsync(string userId, ProfileKind kind, string name, string? inn, CancellationToken ct = default);
    Task RenameAsync(string userId, Guid profileId, string name, CancellationToken ct = default);
    Task SetInnAsync(string userId, Guid profileId, string? inn, CancellationToken ct = default);
}

public interface ILedgerQueries
{
    Task<IReadOnlyList<AccountDto>> AccountsAsync(string userId, Guid? profileId = null, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> CategoriesAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<CounterpartyStatsDto>> CounterpartiesAsync(string userId, string? search = null, CounterpartyKind? kind = null, CancellationToken ct = default);
    Task<CounterpartyDto?> CounterpartyAsync(string userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionDto>> TransactionsAsync(string userId, TransactionFilter filter, CancellationToken ct = default);
    Task<TransactionDetailDto?> TransactionAsync(string userId, Guid id, CancellationToken ct = default);
    Task<SummaryDto> SummaryAsync(string userId, Guid? profileId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AccountCoverageDto>> CoverageAsync(string userId, Guid? profileId = null, CancellationToken ct = default);
}

public interface ILedgerCommands
{
    Task SetCategoryAsync(string userId, Guid transactionId, Guid? categoryId, bool applyToCounterparty, CancellationToken ct = default);
    Task AcceptProposalAsync(string userId, Guid transactionId, CancellationToken ct = default);
    Task SetNoteAsync(string userId, Guid transactionId, string? note, IEnumerable<string> tags, CancellationToken ct = default);

    Task SetAccountFlagsAsync(string userId, Guid accountId, bool includeInCashFlow, bool includeInNetWorth, CancellationToken ct = default);
    Task ArchiveAccountAsync(string userId, Guid accountId, CancellationToken ct = default);
    Task<AccountDto> CreateManualAccountAsync(string userId, Guid profileId, AccountType type, string name, string currency, decimal balance, CancellationToken ct = default);

    Task RenameCounterpartyAsync(string userId, Guid counterpartyId, string name, CancellationToken ct = default);
    Task SetCounterpartyKindAsync(string userId, Guid counterpartyId, CounterpartyKind kind, CancellationToken ct = default);
    Task SetCounterpartyDefaultCategoryAsync(string userId, Guid counterpartyId, Guid? categoryId, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(string userId, string name, CategoryKind kind, string? icon, CancellationToken ct = default);
    Task DeleteAsync(string userId, Guid categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<RuleDto>> RulesAsync(string userId, CancellationToken ct = default);
    Task<RuleDto> AddRuleAsync(string userId, RuleField field, RuleMatch match, string pattern, Guid categoryId, CancellationToken ct = default);
    Task DeleteRuleAsync(string userId, Guid ruleId, CancellationToken ct = default);
}

public interface IImportService
{
    IReadOnlyList<StatementFormatDto> Formats { get; }
    Task<ImportResultDto> ImportAsync(string userId, Guid profileId, Stream file, string fileName, string? bankHint, CancellationToken ct = default);
}

public interface IConnectionsService
{
    IReadOnlyList<ConnectorInfoDto> Connectors();
    Task<IReadOnlyList<ConnectionDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<SyncRunDto>> RunsAsync(string userId, Guid connectionId, int take = 10, CancellationToken ct = default);
    Task<ConnectionDto> CreateAsync(string userId, Guid profileId, ConnectorType type, string name, IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default);
    Task<SyncRunDto> SyncAsync(string userId, Guid connectionId, int initialDays, CancellationToken ct = default);
    Task DeleteAsync(string userId, Guid connectionId, CancellationToken ct = default);
}
