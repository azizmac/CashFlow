using CashFlow.Application.Contracts;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Ledger;

/// <summary>Read-model для экранов. Только чтение, только DTO, всегда в пределах userId.</summary>
public sealed class LedgerQueries : ILedgerQueries
{
    private readonly IUnitOfWork _uow;
    public LedgerQueries(IUnitOfWork uow) => _uow = uow;

    private List<Account> AccountsOf(string userId, Guid? profileId) =>
        _uow.Accounts.Query().Where(a => a.UserId == userId && !a.IsArchived && (profileId == null || a.ProfileId == profileId)).OrderBy(a => a.Name).ToList();

    private Dictionary<Guid, Institution> Institutions() => _uow.Institutions.Query().ToList().ToDictionary(i => i.Id);
    private Dictionary<Guid, FinancialProfile> Profiles(string userId) => _uow.Profiles.Query().Where(p => p.UserId == userId).ToList().ToDictionary(p => p.Id);
    private Dictionary<Guid, Category> CategoryMap(string userId) => _uow.Categories.Query().Where(c => c.UserId == null || c.UserId == userId).ToList().ToDictionary(c => c.Id);

    public Task<IReadOnlyList<AccountDto>> AccountsAsync(string userId, Guid? profileId = null, CancellationToken ct = default)
    {
        var inst = Institutions();
        var profiles = Profiles(userId);
        IReadOnlyList<AccountDto> list = AccountsOf(userId, profileId).Select(a => a.ToDto(inst.GetValueOrDefault(a.InstitutionId), profiles.GetValueOrDefault(a.ProfileId))).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CategoryDto>> CategoriesAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyList<CategoryDto> list = _uow.Categories.Query().Where(c => c.UserId == null || c.UserId == userId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name).ToList().Select(c => c.ToDto()).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CounterpartyStatsDto>> CounterpartiesAsync(string userId, string? search = null, CounterpartyKind? kind = null, CancellationToken ct = default)
    {
        var cps = _uow.Counterparties.Query().Where(c => c.UserId == userId).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = TextNormalizer.Normalize(search);
            cps = cps.Where(c => c.Aliases.Any(a => a.Contains(s)) || (c.Inn?.Contains(s) ?? false)).ToList();
        }
        if (kind is { } k) cps = cps.Where(c => c.Kind == k).ToList();

        var ids = cps.Select(c => c.Id).ToList();
        var stats = _uow.Transactions.Query()
            .Where(t => t.CounterpartyId != null && ids.Contains(t.CounterpartyId!.Value) && t.Kind != TransactionKind.Transfer)
            .Select(t => new { t.CounterpartyId, t.Amount.Amount }).ToList()
            .GroupBy(x => x.CounterpartyId!.Value)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Out: g.Where(x => x.Amount < 0).Sum(x => -x.Amount), In: g.Where(x => x.Amount > 0).Sum(x => x.Amount)));

        IReadOnlyList<CounterpartyStatsDto> list = cps
            .Select(c => { var s = stats.GetValueOrDefault(c.Id); return new CounterpartyStatsDto(c.ToDto(), s.Count, s.Out, s.In); })
            .OrderByDescending(r => r.Out + r.In).ToList();
        return Task.FromResult(list);
    }

    public Task<CounterpartyDto?> CounterpartyAsync(string userId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_uow.Counterparties.Query().FirstOrDefault(c => c.Id == id && c.UserId == userId)?.ToDto());

    public Task<IReadOnlyList<TransactionDto>> TransactionsAsync(string userId, TransactionFilter f, CancellationToken ct = default)
    {
        var (rows, accounts, cps, cats) = Load(userId, f);
        IReadOnlyList<TransactionDto> list = rows.Select(t => t.ToDto(accounts[t.AccountId].Name, t.CounterpartyId is { } id ? cps.GetValueOrDefault(id)?.DisplayName : null, cats)).ToList();
        return Task.FromResult(list);
    }

    private (List<Transaction> Rows, Dictionary<Guid, Account> Accounts, Dictionary<Guid, Counterparty> Cps, Dictionary<Guid, Category> Cats) Load(string userId, TransactionFilter f)
    {
        var accounts = AccountsOf(userId, f.ProfileId).ToDictionary(a => a.Id);
        var accIds = accounts.Keys.ToList();
        var from = new DateTimeOffset(f.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-14);
        var to = new DateTimeOffset(f.To.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddHours(14);

        var q = _uow.Transactions.Query().Where(t => accIds.Contains(t.AccountId) && t.PostedAt >= from && t.PostedAt <= to && t.Status != TransactionStatus.Cancelled);
        if (f.AccountId is { } a) q = q.Where(t => t.AccountId == a);
        if (f.CategoryId is { } c) q = q.Where(t => t.CategoryId == c);
        if (f.CounterpartyId is { } cp) q = q.Where(t => t.CounterpartyId == cp);
        if (f.OnlyUncategorized) q = q.Where(t => t.CategoryId == null && t.Kind != TransactionKind.Transfer);
        if (!f.IncludeTransfers) q = q.Where(t => t.Kind != TransactionKind.Transfer);

        var list = q.OrderByDescending(t => t.PostedAt).Take(f.Take * 2).ToList();
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = TextNormalizer.Normalize(f.Search);
            list = list.Where(t => TextNormalizer.Normalize(t.Description).Contains(s) || TextNormalizer.Normalize(t.CounterpartyRaw.Name).Contains(s) || TextNormalizer.Normalize(t.Purpose).Contains(s)).ToList();
        }
        list = list.Take(f.Take).ToList();

        var cpIds = list.Where(t => t.CounterpartyId != null).Select(t => t.CounterpartyId!.Value).Distinct().ToList();
        var cps = _uow.Counterparties.Query().Where(c => cpIds.Contains(c.Id)).ToList().ToDictionary(c => c.Id);
        return (list, accounts, cps, CategoryMap(userId));
    }

    public async Task<TransactionDetailDto?> TransactionAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var tx = _uow.Transactions.Query().FirstOrDefault(t => t.Id == id);
        if (tx is null) return null;
        var account = _uow.Accounts.Query().FirstOrDefault(a => a.Id == tx.AccountId && a.UserId == userId);
        if (account is null) return null;

        var profile = _uow.Profiles.Query().FirstOrDefault(p => p.Id == account.ProfileId);
        var institution = _uow.Institutions.Query().FirstOrDefault(i => i.Id == account.InstitutionId);
        var cp = tx.CounterpartyId is { } cpId ? _uow.Counterparties.Query().FirstOrDefault(c => c.Id == cpId && c.UserId == userId) : null;
        var cats = CategoryMap(userId);
        var raw = tx.RawRecordId is { } rid ? _uow.RawRecords.Query().FirstOrDefault(r => r.Id == rid) : null;
        var connection = raw is not null ? _uow.Connections.Query().FirstOrDefault(c => c.Id == raw.ConnectionId)
            : account.ConnectionId is { } cid ? _uow.Connections.Query().FirstOrDefault(c => c.Id == cid) : null;

        TransactionDto? pair = null;
        if (tx.TransferLinkId is { } linkId && _uow.TransferLinks.Query().FirstOrDefault(l => l.Id == linkId) is { } link)
        {
            var otherId = link.OutgoingTransactionId == tx.Id ? link.IncomingTransactionId : link.OutgoingTransactionId;
            var other = _uow.Transactions.Query().FirstOrDefault(t => t.Id == otherId);
            var otherAcc = other is null ? null : _uow.Accounts.Query().FirstOrDefault(a => a.Id == other.AccountId && a.UserId == userId);
            if (other is not null && otherAcc is not null) pair = other.ToDto(otherAcc.Name, null, cats);
        }

        // Похожие: тот же контрагент, иначе то же описание
        var postedDate = tx.PostedAt.LocalDate();
        var similarFilter = cp is not null
            ? new TransactionFilter(null, null, null, cp.Id, postedDate.AddYears(-2), postedDate.AddMonths(1), null, false, true, 11)
            : new TransactionFilter(null, null, null, null, postedDate.AddYears(-2), postedDate.AddMonths(1), tx.Description, false, true, 11);
        var similar = (await TransactionsAsync(userId, similarFilter, ct)).Where(r => r.Id != tx.Id).Take(10).ToList();

        var source = new SourceInfoDto(tx.Source, connection?.Name, connection?.SourceCode, raw?.FileName, raw?.CreatedAt, tx.ExternalRef?.ExternalId, raw?.Payload, tx.CreatedAt);
        return new TransactionDetailDto(tx.ToDto(account.Name, cp?.DisplayName, cats), profile?.Name, institution?.Name, Masking.Account(account.AccountNumber),
            cp?.ToDto(), tx.CounterpartyRaw.ToDto(), source, pair, similar);
    }

    public async Task<SummaryDto> SummaryAsync(string userId, Guid? profileId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var accounts = (await AccountsAsync(userId, profileId, ct)).Where(a => a.IncludeInCashFlow).Select(a => a.Id).ToHashSet();
        var rows = (await TransactionsAsync(userId, new TransactionFilter(profileId, null, null, null, from, to, null, false, false, 100_000), ct))
            .Where(r => accounts.Contains(r.AccountId)).ToList();

        var income = rows.Where(r => r.IsIncome).Sum(r => r.Amount);
        var expense = rows.Where(r => r.IsExpense).Sum(r => -r.Amount);
        var byCat = rows.Where(r => r.IsExpense).GroupBy(r => r.CategoryId)
            .Select(g => new CategoryTotalDto(g.Key, g.First().CategoryLabel ?? "Без категории", g.Sum(r => -r.Amount), g.Count()))
            .OrderByDescending(x => x.Total).ToList();
        var cpKinds = _uow.Counterparties.Query().Where(c => c.UserId == userId).Select(c => new { c.Id, c.Kind }).ToList().ToDictionary(x => x.Id, x => x.Kind);
        var byCp = rows.Where(r => r.IsExpense && r.CounterpartyId is not null).GroupBy(r => r.CounterpartyId!.Value)
            .Select(g => new CounterpartyTotalDto(g.Key, g.First().CounterpartyName ?? "", cpKinds.GetValueOrDefault(g.Key), g.Sum(r => -r.Amount), g.Count()))
            .OrderByDescending(x => x.Total).Take(15).ToList();

        var netWorth = (await AccountsAsync(userId, profileId, ct)).Where(a => a.IncludeInNetWorth && a.Balance != null)
            .Sum(a => a.Type is AccountType.Loan or AccountType.CreditCard ? -Math.Abs(a.Balance!.Value) : a.Balance!.Value);

        return new SummaryDto(income, expense, netWorth, byCat, byCp);
    }

    public async Task<IReadOnlyList<AccountCoverageDto>> CoverageAsync(string userId, Guid? profileId = null, CancellationToken ct = default)
    {
        var accounts = await AccountsAsync(userId, profileId, ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var stats = _uow.Transactions.Query().Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Min = g.Min(t => t.PostedAt), Max = g.Max(t => t.PostedAt), Count = g.Count() })
            .ToList().ToDictionary(x => x.AccountId);
        var sources = _uow.Transactions.Query().Where(t => ids.Contains(t.AccountId) && t.RawRecordId != null)
            .Join(_uow.RawRecords.Query(), t => t.RawRecordId, r => r.Id, (t, r) => new { t.AccountId, r.ConnectionId })
            .Distinct().ToList();
        var ownConn = _uow.Accounts.Query().Where(a => ids.Contains(a.Id) && a.ConnectionId != null).Select(a => new { a.Id, a.ConnectionId }).ToList();
        var connIds = sources.Select(s => s.ConnectionId).Concat(ownConn.Select(x => x.ConnectionId!.Value)).Distinct().ToList();
        var conns = _uow.Connections.Query().Where(c => connIds.Contains(c.Id)).ToList().ToDictionary(c => c.Id);
        var today = DateOnly.FromDateTime(DateTime.Today);

        return accounts.Select(a =>
        {
            var s = stats.GetValueOrDefault(a.Id);
            var src = sources.Where(x => x.AccountId == a.Id).Select(x => conns.GetValueOrDefault(x.ConnectionId)).Where(c => c is not null).Select(c => c!).ToList();
            if (src.Count == 0 && ownConn.FirstOrDefault(x => x.Id == a.Id)?.ConnectionId is { } cid && conns.GetValueOrDefault(cid) is { } own) src.Add(own);
            var lastImport = src.Select(c => c.LastSyncAt).Where(d => d is not null).DefaultIfEmpty(null).Max();
            DateOnly? last = s is null ? null : s.Max.LocalDate();
            var stale = last is { } l ? Math.Max(0, today.DayNumber - l.DayNumber) : int.MaxValue;
            return new AccountCoverageDto(a, s is null ? null : s.Min.LocalDate(), last, s?.Count ?? 0, lastImport, src.Select(c => c.Name).Distinct().ToList(), stale);
        }).OrderBy(c => c.Account.ProfileName).ThenBy(c => c.Account.Name).ToList();
    }
}
