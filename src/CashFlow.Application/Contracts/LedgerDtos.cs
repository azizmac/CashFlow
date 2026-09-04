using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Contracts;

// DTO — единственное, что уходит за границу Application: в Blazor-страницы, в API, в MAUI.
// Правила: никаких доменных сущностей наружу, никаких ссылок на секреты (CredentialRef) и чужих данных;
// номера счетов и телефоны только маскированные, кроме случаев, где это данные самого пользователя (ИНН профиля).

public sealed record ProfileDto(Guid Id, string Name, ProfileKind Kind, string? Inn);

public sealed record AccountDto(
    Guid Id, Guid ProfileId, string ProfileName, string BankCode, string BankName, AccountType Type, string Name, string Currency,
    string? NumberMasked, decimal? Balance, DateTimeOffset? BalanceAt, bool IncludeInCashFlow, bool IncludeInNetWorth);

public sealed record CategoryDto(Guid Id, string Name, CategoryKind Kind, Guid? ParentId, string? Icon, string? Color, bool IsSystem, string? Code)
{
    public string Label => string.IsNullOrEmpty(Icon) ? Name : $"{Icon} {Name}";
}

public sealed record RuleDto(Guid Id, RuleField Field, RuleMatch Match, string Pattern, Guid CategoryId, int Priority, RuleOrigin Origin, bool IsEnabled, int HitCount);

public sealed record CounterpartyDto(
    Guid Id, string DisplayName, CounterpartyKind Kind, string? Inn, string? Kpp, Guid? DefaultCategoryId, bool IsFavorite, string? Note,
    IReadOnlyList<string> Aliases, IReadOnlyList<string> AccountsMasked, IReadOnlyList<string> PhonesMasked);

public sealed record CounterpartyStatsDto(CounterpartyDto Counterparty, int Count, decimal Out, decimal In);

public sealed record TransactionDto(
    Guid Id, Guid AccountId, string AccountName, DateTimeOffset PostedAt, DateTimeOffset? BookedAt, decimal Amount, string Currency,
    TransactionStatus Status, TransactionKind Kind, string Description, string? Purpose, string? Mcc,
    Guid? CounterpartyId, string? CounterpartyName,
    Guid? CategoryId, string? CategoryLabel, CategorySource CategorySource, decimal? CategoryConfidence,
    Guid? ProposedCategoryId, string? ProposedCategoryLabel, bool ReviewedByUser, string? Note, IReadOnlyList<string> Tags)
{
    public bool IsIncome => Amount > 0;
    public bool IsExpense => Amount < 0;
    public bool IsTransfer => Kind == TransactionKind.Transfer;
}

public sealed record CounterpartyRawDto(string? Name, string? Inn, string? Kpp, string? AccountMasked, string? Bik, string? BankName, string? PhoneMasked)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && Inn is null && AccountMasked is null && PhoneMasked is null;
}

/// <summary>Откуда пришла операция. Сырая запись — данные самого пользователя, отдаётся только в карточке одной операции.</summary>
public sealed record SourceInfoDto(ConnectorType Source, string? ConnectionName, string? SourceCode, string? FileName, DateTimeOffset? LoadedAt, string? ExternalId, string? RawPayload, DateTimeOffset ImportedAt);

public sealed record TransactionDetailDto(
    TransactionDto Transaction, string? ProfileName, string? BankName, string? AccountNumberMasked,
    CounterpartyDto? Counterparty, CounterpartyRawDto Raw, SourceInfoDto Source, TransactionDto? Pair, IReadOnlyList<TransactionDto> Similar);

public sealed record TransactionFilter(
    Guid? ProfileId, Guid? AccountId, Guid? CategoryId, Guid? CounterpartyId, DateOnly From, DateOnly To,
    string? Search, bool OnlyUncategorized, bool IncludeTransfers, int Take = 500);

public sealed record CategoryTotalDto(Guid? CategoryId, string Label, decimal Total, int Count);
public sealed record CounterpartyTotalDto(Guid CounterpartyId, string Name, CounterpartyKind Kind, decimal Total, int Count);
public sealed record SummaryDto(decimal Income, decimal Expense, decimal NetWorth, IReadOnlyList<CategoryTotalDto> ByCategory, IReadOnlyList<CounterpartyTotalDto> ByCounterparty);

/// <summary>Покрытие счёта данными: за какой период есть операции, откуда они и насколько устарели.</summary>
public sealed record AccountCoverageDto(AccountDto Account, DateOnly? First, DateOnly? Last, int Count, DateTimeOffset? LastImportAt, IReadOnlyList<string> Sources, int StaleDays)
{
    public bool IsStale => Count == 0 || StaleDays > 30;
}

public static class Masking
{
    public static string? Account(string? n) => string.IsNullOrEmpty(n) ? null : n.Length > 4 ? "…" + n[^4..] : n;
    public static string? Phone(string? p) => string.IsNullOrEmpty(p) ? null : p.Length > 4 ? "+7 ••• ••• " + p[^4..] : p;
}
