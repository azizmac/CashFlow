using CashFlow.Application;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Web.Services;

public sealed record TxRow(Transaction Tx, Account Account, Counterparty? Counterparty, Category? Category, Category? Proposed);
public sealed record CategoryTotal(Category? Category, decimal Total, int Count);
public sealed record CounterpartyTotal(Counterparty Counterparty, decimal Total, int Count);
public sealed record TxFilter(Guid? ProfileId, Guid? AccountId, Guid? CategoryId, Guid? CounterpartyId, DateOnly From, DateOnly To, string? Search, bool OnlyUncategorized, bool IncludeTransfers);

/// <summary>Read-model для страниц. Read-only.</summary>
public static class Tz
{
    public static readonly TimeZoneInfo Display = TimeZoneInfo.FindSystemTimeZoneById(Environment.GetEnvironmentVariable("CASHFLOW_TZ") ?? "Europe/Moscow");
    public static DateTimeOffset Local(this DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Display);
}

public sealed class LedgerQueries
{
    private readonly IUnitOfWork _uow;
    public LedgerQueries(IUnitOfWork uow) => _uow = uow;

    public List<Account> Accounts(string userId, Guid? profileId = null) =>
        _uow.Accounts.Query().Where(a => a.UserId == userId && !a.IsArchived && (profileId == null || a.ProfileId == profileId)).OrderBy(a => a.Name).ToList();

    public List<Institution> Institutions() => _uow.Institutions.Query().OrderBy(i => i.Name).ToList();

    public List<Category> Categories(string userId) =>
        _uow.Categories.Query().Where(c => c.UserId == null || c.UserId == userId).OrderBy(c => c.Kind).ThenBy(c => c.Name).ToList();

    public List<Counterparty> Counterparties(string userId) =>
        _uow.Counterparties.Query().Where(c => c.UserId == userId).OrderBy(c => c.DisplayName).ToList();

    public List<Connection> Connections(string userId) => _uow.Connections.Query().Where(c => c.UserId == userId).OrderBy(c => c.Name).ToList();

    public List<SyncRun> SyncRuns(Guid connectionId, int take = 10) =>
        _uow.SyncRuns.Query().Where(s => s.ConnectionId == connectionId).OrderByDescending(s => s.StartedAt).Take(take).ToList();

    public List<TxRow> Transactions(string userId, TxFilter f, int take = 500)
    {
        var accounts = Accounts(userId, f.ProfileId).ToDictionary(a => a.Id);
        var accIds = accounts.Keys.ToList();
        var from = new DateTimeOffset(f.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-14);
        var to = new DateTimeOffset(f.To.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddHours(14);

        var q = _uow.Transactions.Query().Where(t => accIds.Contains(t.AccountId) && t.PostedAt >= from && t.PostedAt <= to && t.Status != TransactionStatus.Cancelled);
        if (f.AccountId is { } a) q = q.Where(t => t.AccountId == a);
        if (f.CategoryId is { } c) q = q.Where(t => t.CategoryId == c);
        if (f.CounterpartyId is { } cp) q = q.Where(t => t.CounterpartyId == cp);
        if (f.OnlyUncategorized) q = q.Where(t => t.CategoryId == null && t.Kind != TransactionKind.Transfer);
        if (!f.IncludeTransfers) q = q.Where(t => t.Kind != TransactionKind.Transfer);

        var list = q.OrderByDescending(t => t.PostedAt).Take(take * 2).ToList();
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = TextNormalizer.Normalize(f.Search);
            list = list.Where(t => TextNormalizer.Normalize(t.Description).Contains(s) || TextNormalizer.Normalize(t.CounterpartyRaw.Name).Contains(s) || TextNormalizer.Normalize(t.Purpose).Contains(s)).ToList();
        }
        list = list.Take(take).ToList();

        var cats = Categories(userId).ToDictionary(c => c.Id);
        var cpIds = list.Where(t => t.CounterpartyId != null).Select(t => t.CounterpartyId!.Value).Distinct().ToList();
        var cps = _uow.Counterparties.Query().Where(c => cpIds.Contains(c.Id)).ToDictionary(c => c.Id);

        return list.Select(t => new TxRow(t, accounts[t.AccountId],
            t.CounterpartyId is { } id ? cps.GetValueOrDefault(id) : null,
            t.CategoryId is { } cid ? cats.GetValueOrDefault(cid) : null,
            t.ProposedCategoryId is { } pid ? cats.GetValueOrDefault(pid) : null)).ToList();
    }

    public (decimal Income, decimal Expense, List<CategoryTotal> ByCategory, List<CounterpartyTotal> ByCounterparty) Summary(string userId, Guid? profileId, DateOnly from, DateOnly to)
    {
        var rows = Transactions(userId, new TxFilter(profileId, null, null, null, from, to, null, false, false), take: 100_000)
            .Where(r => r.Account.IncludeInCashFlow).ToList();

        var income = rows.Where(r => r.Tx.IsIncome).Sum(r => r.Tx.Amount.Amount);
        var expense = rows.Where(r => r.Tx.IsExpense).Sum(r => -r.Tx.Amount.Amount);

        var byCat = rows.Where(r => r.Tx.IsExpense)
            .GroupBy(r => r.Category?.Id)
            .Select(g => new CategoryTotal(g.First().Category, g.Sum(r => -r.Tx.Amount.Amount), g.Count()))
            .OrderByDescending(x => x.Total).ToList();

        var byCp = rows.Where(r => r.Tx.IsExpense && r.Counterparty is not null)
            .GroupBy(r => r.Counterparty!.Id)
            .Select(g => new CounterpartyTotal(g.First().Counterparty!, g.Sum(r => -r.Tx.Amount.Amount), g.Count()))
            .OrderByDescending(x => x.Total).Take(15).ToList();

        return (income, expense, byCat, byCp);
    }

    public decimal NetWorth(string userId, Guid? profileId) =>
        Accounts(userId, profileId).Where(a => a.IncludeInNetWorth && a.LastBalance != null)
            .Sum(a => a.Type is AccountType.Loan or AccountType.CreditCard ? -Math.Abs(a.LastBalance!.Amount) : a.LastBalance!.Amount);
}
