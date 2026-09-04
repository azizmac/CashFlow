using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Contracts;

// Тела запросов API. Живут рядом с DTO, чтобы MAUI-клиент и сервер использовали одни и те же типы.

public sealed record CreateProfileRequest(ProfileKind Kind, string Name, string? Inn);
public sealed record RenameRequest(string Name);
public sealed record SetInnRequest(string? Inn);

public sealed record SetCategoryRequest(Guid? CategoryId, bool ApplyToCounterparty);
public sealed record SetNoteRequest(string? Note, IReadOnlyList<string> Tags);

public sealed record AccountFlagsRequest(bool IncludeInCashFlow, bool IncludeInNetWorth);
public sealed record CreateManualAccountRequest(Guid ProfileId, AccountType Type, string Name, string Currency, decimal Balance);

public sealed record SetCounterpartyKindRequest(CounterpartyKind Kind);
public sealed record SetDefaultCategoryRequest(Guid? CategoryId);

public sealed record CreateCategoryRequest(string Name, CategoryKind Kind, string? Icon);
public sealed record CreateRuleRequest(RuleField Field, RuleMatch Match, string Pattern, Guid CategoryId);

public sealed record CreateConnectionRequest(Guid ProfileId, ConnectorType Type, string Name, Dictionary<string, string> Secrets);

/// <summary>Ответ API при ошибке бизнес-логики: 400/403/404 с человекочитаемым сообщением.</summary>
public sealed record ApiError(string Error);
