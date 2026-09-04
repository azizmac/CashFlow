using System.Globalization;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Client;

// Реализации контрактов Application поверх REST. Параметр userId игнорируется: сервер берёт пользователя из токена.

public sealed class HttpProfileService : IProfileService
{
    private readonly ApiClient _api;
    public HttpProfileService(ApiClient api) => _api = api;

    public Task<IReadOnlyList<ProfileDto>> ListAsync(string userId, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<ProfileDto>>("api/profiles", ct);
    public Task<ProfileDto> CreateAsync(string userId, ProfileKind kind, string name, string? inn, CancellationToken ct = default) => _api.PostAsync<ProfileDto>("api/profiles", new CreateProfileRequest(kind, name, inn), ct);
    public Task RenameAsync(string userId, Guid profileId, string name, CancellationToken ct = default) => _api.PutAsync($"api/profiles/{profileId}/name", new RenameRequest(name), ct);
    public Task SetInnAsync(string userId, Guid profileId, string? inn, CancellationToken ct = default) => _api.PutAsync($"api/profiles/{profileId}/inn", new SetInnRequest(inn), ct);
}

public sealed class HttpLedgerQueries : ILedgerQueries
{
    private readonly ApiClient _api;
    public HttpLedgerQueries(ApiClient api) => _api = api;

    public Task<IReadOnlyList<AccountDto>> AccountsAsync(string userId, Guid? profileId = null, CancellationToken ct = default) =>
        _api.GetAsync<IReadOnlyList<AccountDto>>("api/accounts" + Q(("profileId", profileId)), ct);

    public Task<IReadOnlyList<CategoryDto>> CategoriesAsync(string userId, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<CategoryDto>>("api/categories", ct);

    public Task<IReadOnlyList<CounterpartyStatsDto>> CounterpartiesAsync(string userId, string? search = null, CounterpartyKind? kind = null, CancellationToken ct = default) =>
        _api.GetAsync<IReadOnlyList<CounterpartyStatsDto>>("api/counterparties" + Q(("search", search), ("kind", kind)), ct);

    public Task<CounterpartyDto?> CounterpartyAsync(string userId, Guid id, CancellationToken ct = default) => _api.GetOrDefaultAsync<CounterpartyDto>($"api/counterparties/{id}", ct);

    public Task<IReadOnlyList<TransactionDto>> TransactionsAsync(string userId, TransactionFilter f, CancellationToken ct = default) =>
        _api.GetAsync<IReadOnlyList<TransactionDto>>("api/transactions" + Q(
            ("profileId", f.ProfileId), ("accountId", f.AccountId), ("categoryId", f.CategoryId), ("counterpartyId", f.CounterpartyId),
            ("from", f.From), ("to", f.To), ("search", f.Search), ("onlyUncategorized", f.OnlyUncategorized), ("includeTransfers", f.IncludeTransfers), ("take", f.Take)), ct);

    public Task<TransactionDetailDto?> TransactionAsync(string userId, Guid id, CancellationToken ct = default) => _api.GetOrDefaultAsync<TransactionDetailDto>($"api/transactions/{id}", ct);

    public Task<SummaryDto> SummaryAsync(string userId, Guid? profileId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        _api.GetAsync<SummaryDto>("api/summary" + Q(("profileId", profileId), ("from", from), ("to", to)), ct);

    public Task<IReadOnlyList<AccountCoverageDto>> CoverageAsync(string userId, Guid? profileId = null, CancellationToken ct = default) =>
        _api.GetAsync<IReadOnlyList<AccountCoverageDto>>("api/coverage" + Q(("profileId", profileId)), ct);

    /// <summary>Query string из непустых значений: даты в ISO, enum и bool как есть.</summary>
    internal static string Q(params (string Name, object? Value)[] items)
    {
        var parts = items.Where(i => i.Value is not null && i.Value is not "").Select(i =>
        {
            var v = i.Value switch
            {
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => Convert.ToString(i.Value, CultureInfo.InvariantCulture) ?? "",
            };
            return $"{i.Name}={Uri.EscapeDataString(v)}";
        }).ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }
}

public sealed class HttpLedgerCommands : ILedgerCommands
{
    private readonly ApiClient _api;
    public HttpLedgerCommands(ApiClient api) => _api = api;

    public Task SetCategoryAsync(string userId, Guid transactionId, Guid? categoryId, bool applyToCounterparty, CancellationToken ct = default) =>
        _api.PutAsync($"api/transactions/{transactionId}/category", new SetCategoryRequest(categoryId, applyToCounterparty), ct);
    public Task AcceptProposalAsync(string userId, Guid transactionId, CancellationToken ct = default) => _api.PostAsync($"api/transactions/{transactionId}/accept-proposal", null, ct);
    public Task SetNoteAsync(string userId, Guid transactionId, string? note, IEnumerable<string> tags, CancellationToken ct = default) =>
        _api.PutAsync($"api/transactions/{transactionId}/note", new SetNoteRequest(note, tags.ToList()), ct);
    public Task SetAccountFlagsAsync(string userId, Guid accountId, bool includeInCashFlow, bool includeInNetWorth, CancellationToken ct = default) =>
        _api.PutAsync($"api/accounts/{accountId}/flags", new AccountFlagsRequest(includeInCashFlow, includeInNetWorth), ct);
    public Task ArchiveAccountAsync(string userId, Guid accountId, CancellationToken ct = default) => _api.PostAsync($"api/accounts/{accountId}/archive", null, ct);
    public Task<AccountDto> CreateManualAccountAsync(string userId, Guid profileId, AccountType type, string name, string currency, decimal balance, CancellationToken ct = default) =>
        _api.PostAsync<AccountDto>("api/accounts/manual", new CreateManualAccountRequest(profileId, type, name, currency, balance), ct);
    public Task RenameCounterpartyAsync(string userId, Guid counterpartyId, string name, CancellationToken ct = default) => _api.PutAsync($"api/counterparties/{counterpartyId}/name", new RenameRequest(name), ct);
    public Task SetCounterpartyKindAsync(string userId, Guid counterpartyId, CounterpartyKind kind, CancellationToken ct = default) => _api.PutAsync($"api/counterparties/{counterpartyId}/kind", new SetCounterpartyKindRequest(kind), ct);
    public Task SetCounterpartyDefaultCategoryAsync(string userId, Guid counterpartyId, Guid? categoryId, CancellationToken ct = default) => _api.PutAsync($"api/counterparties/{counterpartyId}/default-category", new SetDefaultCategoryRequest(categoryId), ct);
}

public sealed class HttpCategoryService : ICategoryService
{
    private readonly ApiClient _api;
    public HttpCategoryService(ApiClient api) => _api = api;

    public Task<IReadOnlyList<CategoryDto>> ListAsync(string userId, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<CategoryDto>>("api/categories", ct);
    public Task<CategoryDto> CreateAsync(string userId, string name, CategoryKind kind, string? icon, CancellationToken ct = default) => _api.PostAsync<CategoryDto>("api/categories", new CreateCategoryRequest(name, kind, icon), ct);
    public Task DeleteAsync(string userId, Guid categoryId, CancellationToken ct = default) => _api.DeleteAsync($"api/categories/{categoryId}", ct);
    public Task<IReadOnlyList<RuleDto>> RulesAsync(string userId, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<RuleDto>>("api/categories/rules", ct);
    public Task<RuleDto> AddRuleAsync(string userId, RuleField field, RuleMatch match, string pattern, Guid categoryId, CancellationToken ct = default) => _api.PostAsync<RuleDto>("api/categories/rules", new CreateRuleRequest(field, match, pattern, categoryId), ct);
    public Task DeleteRuleAsync(string userId, Guid ruleId, CancellationToken ct = default) => _api.DeleteAsync($"api/categories/rules/{ruleId}", ct);
}

public sealed class HttpImportService : IImportService
{
    private readonly ApiClient _api;
    private IReadOnlyList<StatementFormatDto>? _formats;
    public HttpImportService(ApiClient api) => _api = api;

    /// <summary>Список форматов кэшируется после первого запроса; до него пустой (страница показывает общий текст).</summary>
    public IReadOnlyList<StatementFormatDto> Formats
    {
        get
        {
            if (_formats is null) _ = LoadFormatsAsync();
            return _formats ?? [];
        }
    }

    public async Task LoadFormatsAsync()
    {
        try { _formats = await _api.GetAsync<IReadOnlyList<StatementFormatDto>>("api/import/formats"); }
        catch { _formats ??= []; }
    }

    public async Task<ImportResultDto> ImportAsync(string userId, Guid profileId, Stream file, string fileName, string? bankHint, CancellationToken ct = default)
    {
        var results = await _api.PostFileAsync<IReadOnlyList<ImportResultDto>>($"api/import?profileId={profileId}" + (bankHint is null ? "" : $"&bank={Uri.EscapeDataString(bankHint)}"), file, fileName, ct);
        return results.FirstOrDefault() ?? throw new ApiException(500, "Сервер не вернул результат импорта");
    }
}

public sealed class HttpConnectionsService : IConnectionsService
{
    private readonly ApiClient _api;
    private IReadOnlyList<ConnectorInfoDto>? _connectors;
    public HttpConnectionsService(ApiClient api) => _api = api;

    public IReadOnlyList<ConnectorInfoDto> Connectors()
    {
        if (_connectors is null) _ = LoadConnectorsAsync();
        return _connectors ?? [];
    }

    public async Task LoadConnectorsAsync()
    {
        try { _connectors = await _api.GetAsync<IReadOnlyList<ConnectorInfoDto>>("api/connections/connectors"); }
        catch { _connectors ??= []; }
    }

    public Task<IReadOnlyList<ConnectionDto>> ListAsync(string userId, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<ConnectionDto>>("api/connections", ct);
    public Task<IReadOnlyList<SyncRunDto>> RunsAsync(string userId, Guid connectionId, int take = 10, CancellationToken ct = default) => _api.GetAsync<IReadOnlyList<SyncRunDto>>($"api/connections/{connectionId}/runs?take={take}", ct);
    public Task<ConnectionDto> CreateAsync(string userId, Guid profileId, ConnectorType type, string name, IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default) =>
        _api.PostAsync<ConnectionDto>("api/connections", new CreateConnectionRequest(profileId, type, name, new Dictionary<string, string>(secrets)), ct);
    public Task<SyncRunDto> SyncAsync(string userId, Guid connectionId, int initialDays, CancellationToken ct = default) => _api.PostAsync<SyncRunDto>($"api/connections/{connectionId}/sync?initialDays={initialDays}", null, ct);
    public Task DeleteAsync(string userId, Guid connectionId, CancellationToken ct = default) => _api.DeleteAsync($"api/connections/{connectionId}", ct);
}
