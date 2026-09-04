using CashFlow.Application;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Web.Services;

public sealed record TxRow(Transaction Tx, Account Account, Counterparty? Counterparty, Category? Category, Category? Proposed);
public sealed record CategoryTotal(Category? Category, decimal Total, int Count);
public sealed record CounterpartyTotal(Counterparty Counterparty, decimal Total, int Count);
public sealed record TxFilter(Guid? ProfileId, Guid? AccountId, Guid? CategoryId, Guid? CounterpartyId, DateOnly From, DateOnly To, string? Search, bool OnlyUncategorized, bool IncludeTransfers);

/// <summary>Карточка операции: всё, что о ней известно, включая источник и парную операцию перевода.</summary>
public sealed record TxDetail(Transaction Tx, Account Account, FinancialProfile? Profile, Institution? Institution, Counterparty? Counterparty,
    Category? Category, Category? Proposed, Connection? Connection, RawRecord? Raw, Transaction? Pair, Account? PairAccount, List<TxRow> Similar);

/// <summary>Покрытие счёта данными: за какой период есть операции, откуда они пришли и насколько данные устарели.</summary>
public sealed record AccountCoverage(Account Account, Institution? Institution, FinancialProfile? Profile, DateOnly? First, DateOnly? Last, int Count,
    DateTimeOffset? LastImportAt, IReadOnlyList<string> Sources)
{
    public int StaleDays => Last is { } l ? Math.Max(0, DateOnly.FromDateTime(DateTime.Today).DayNumber - l.DayNumber) : int.MaxValue;
    public bool IsStale => Count == 0 || StaleDays > 30;
}

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

    public TxDetail? TransactionDetail(string userId, Guid id)
    {
        var tx = _uow.Transactions.Query().FirstOrDefault(t => t.Id == id);
        if (tx is null) return null;
        var account = _uow.Accounts.Query().FirstOrDefault(a => a.Id == tx.AccountId && a.UserId == userId);
        if (account is null) return null;

        var profile = _uow.Profiles.Query().FirstOrDefault(p => p.Id == account.ProfileId);
        var institution = _uow.Institutions.Query().FirstOrDefault(i => i.Id == account.InstitutionId);
        var cp = tx.CounterpartyId is { } cpId ? _uow.Counterparties.Query().FirstOrDefault(c => c.Id == cpId) : null;
        var cats = Categories(userId).ToDictionary(c => c.Id);
        var raw = tx.RawRecordId is { } rid ? _uow.RawRecords.Query().FirstOrDefault(r => r.Id == rid) : null;
        var connection = raw is not null ? _uow.Connections.Query().FirstOrDefault(c => c.Id == raw.ConnectionId)
            : account.ConnectionId is { } cid ? _uow.Connections.Query().FirstOrDefault(c => c.Id == cid) : null;

        Transaction? pair = null; Account? pairAccount = null;
        if (tx.TransferLinkId is { } linkId)
        {
            var link = _uow.TransferLinks.Query().FirstOrDefault(l => l.Id == linkId);
            var otherId = link is null ? (Guid?)null : link.OutgoingTransactionId == tx.Id ? link.IncomingTransactionId : link.OutgoingTransactionId;
            if (otherId is { } oid)
            {
                pair = _uow.Transactions.Query().FirstOrDefault(t => t.Id == oid);
                if (pair is not null) pairAccount = _uow.Accounts.Query().FirstOrDefault(a => a.Id == pair.AccountId);
            }
        }

        // Похожие: тот же контрагент, иначе то же нормализованное описание
        var similar = new List<TxRow>();
        if (cp is not null)
            similar = Transactions(userId, new TxFilter(null, null, null, cp.Id, tx.PostedDate.AddYears(-2), tx.PostedDate.AddMonths(1), null, false, true), 11).Where(r => r.Tx.Id != tx.Id).Take(10).ToList();
        else
        {
            var norm = TextNormalizer.Normalize(tx.Description);
            if (norm.Length >= 5)
                similar = Transactions(userId, new TxFilter(null, null, null, null, tx.PostedDate.AddYears(-2), tx.PostedDate.AddMonths(1), tx.Description, false, true), 11).Where(r => r.Tx.Id != tx.Id).Take(10).ToList();
        }

        return new TxDetail(tx, account, profile, institution, cp,
            tx.CategoryId is { } c1 ? cats.GetValueOrDefault(c1) : null,
            tx.ProposedCategoryId is { } c2 ? cats.GetValueOrDefault(c2) : null,
            connection, raw, pair, pairAccount, similar);
    }

    /// <summary>Покрытие данными по каждому счёту: период операций, число, дата последнего импорта, источники (подключения).</summary>
    public List<AccountCoverage> Coverage(string userId, Guid? profileId = null)
    {
        var accounts = Accounts(userId, profileId);
        var ids = accounts.Select(a => a.Id).ToList();
        var stats = _uow.Transactions.Query().Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Min = g.Min(t => t.PostedAt), Max = g.Max(t => t.PostedAt), Count = g.Count() })
            .ToList().ToDictionary(x => x.AccountId);
        var sources = _uow.Transactions.Query().Where(t => ids.Contains(t.AccountId) && t.RawRecordId != null)
            .Join(_uow.RawRecords.Query(), t => t.RawRecordId, r => r.Id, (t, r) => new { t.AccountId, r.ConnectionId })
            .Distinct().ToList();
        var connIds = sources.Select(s => s.ConnectionId).Distinct().ToList();
        var conns = _uow.Connections.Query().Where(c => connIds.Contains(c.Id)).ToList().ToDictionary(c => c.Id);
        var institutions = Institutions().ToDictionary(i => i.Id);
        var profiles = _uow.Profiles.Query().Where(p => p.UserId == userId).ToList().ToDictionary(p => p.Id);

        return accounts.Select(a =>
        {
            var s = stats.GetValueOrDefault(a.Id);
            var src = sources.Where(x => x.AccountId == a.Id).Select(x => conns.GetValueOrDefault(x.ConnectionId)).Where(c => c is not null).ToList();
            if (src.Count == 0 && a.ConnectionId is { } cid && _uow.Connections.Query().FirstOrDefault(c => c.Id == cid) is { } own) src.Add(own);
            var lastImport = src.Select(c => c!.LastSyncAt).Where(d => d is not null).DefaultIfEmpty(null).Max();
            return new AccountCoverage(a, institutions.GetValueOrDefault(a.InstitutionId), profiles.GetValueOrDefault(a.ProfileId),
                s is null ? null : DateOnly.FromDateTime(s.Min.Local().DateTime), s is null ? null : DateOnly.FromDateTime(s.Max.Local().DateTime), s?.Count ?? 0,
                lastImport, src.Select(c => c!.Name).Distinct().ToList());
        }).OrderBy(c => c.Profile?.Name).ThenBy(c => c.Account.Name).ToList();
    }

    public decimal NetWorth(string userId, Guid? profileId) =>
        Accounts(userId, profileId).Where(a => a.IncludeInNetWorth && a.LastBalance != null)
            .Sum(a => a.Type is AccountType.Loan or AccountType.CreditCard ? -Math.Abs(a.LastBalance!.Amount) : a.LastBalance!.Amount);
}
