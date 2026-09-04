using CashFlow.Application.Contracts;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;

namespace CashFlow.Application.Ledger;

/// <summary>Маппинг сущностей в DTO. Здесь же решается, что наружу не уходит (номера, телефоны, CredentialRef).</summary>
internal static class Mapping
{
    public static ProfileDto ToDto(this FinancialProfile p) => new(p.Id, p.Name, p.Kind, p.Inn);

    public static AccountDto ToDto(this Account a, Institution? inst, FinancialProfile? profile) => new(
        a.Id, a.ProfileId, profile?.Name ?? "", inst?.Code ?? "", inst?.Name ?? "", a.Type, a.Name, a.Currency.Code,
        Masking.Account(a.AccountNumber), a.LastBalance?.Amount, a.LastBalanceAt, a.IncludeInCashFlow, a.IncludeInNetWorth);

    public static CategoryDto ToDto(this Category c) => new(c.Id, c.Name, c.Kind, c.ParentId, c.Icon, c.Color, c.IsSystem, c.Code);

    public static RuleDto ToDto(this CategorizationRule r) => new(r.Id, r.Field, r.Match, r.Pattern, r.CategoryId, r.Priority, r.Origin, r.IsEnabled, r.HitCount);

    public static CounterpartyDto ToDto(this Counterparty c) => new(
        c.Id, c.DisplayName, c.Kind, c.Inn, c.Kpp, c.DefaultCategoryId, c.IsFavorite, c.Note,
        c.Aliases, c.Accounts.Select(a => Masking.Account(a)!).ToList(), c.Phones.Select(p => Masking.Phone(p)!).ToList());

    public static CounterpartyRawDto ToDto(this CounterpartyRaw r) =>
        new(r.Name, r.Inn, r.Kpp, Masking.Account(r.Account), r.Bik, r.BankName, Masking.Phone(r.Phone));

    public static TransactionDto ToDto(this Transaction t, string accountName, string? counterpartyName, IReadOnlyDictionary<Guid, Category> categories) => new(
        t.Id, t.AccountId, accountName, t.PostedAt, t.BookedAt, t.Amount.Amount, t.Amount.Currency.Code, t.Status, t.Kind, t.Description, t.Purpose, t.Mcc,
        t.CounterpartyId, counterpartyName,
        t.CategoryId, t.CategoryId is { } c && categories.TryGetValue(c, out var cat) ? cat.ToDto().Label : null, t.CategorySource, t.CategoryConfidence,
        t.ProposedCategoryId, t.ProposedCategoryId is { } p && categories.TryGetValue(p, out var prop) ? prop.ToDto().Label : null,
        t.ReviewedByUser, t.Note, t.Tags);

    public static ConnectionDto ToDto(this Connection c, string profileName) =>
        new(c.Id, c.ProfileId, profileName, c.Name, c.ConnectorType, c.SourceCode, c.Status, c.LastError, c.LastSyncAt, c.SyncCursor);

    public static SyncRunDto ToDto(this SyncRun r) => new(r.Id, r.StartedAt, r.FinishedAt, r.Result, r.ImportedTransactions, r.SkippedDuplicates, r.Error);
}
