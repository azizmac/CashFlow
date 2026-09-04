using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Ledger.Services;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Import;

public sealed record ImportSummary(int Imported, int Updated, int SkippedDuplicates, int CounterpartiesCreated, int TransfersLinked, int Categorized);

/// <summary>
/// Единая точка входа для операций из любого источника (API или файл):
/// дедуп → сохранение сырых данных → контрагенты → переводы → категоризация.
/// </summary>
public sealed class TransactionImportService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TransactionImportService> _log;

    public TransactionImportService(IUnitOfWork uow, ILogger<TransactionImportService> log)
    {
        _uow = uow;
        _log = log;
    }

    public async Task<ImportSummary> ImportAsync(
        string userId,
        Account account,
        Connection? connection,
        ConnectorType source,
        IReadOnlyList<ExternalTransaction> external,
        string? fileName,
        CancellationToken ct)
    {
        var existing = _uow.Transactions.Query().Where(t => t.AccountId == account.Id).ToList();
        var byKey = existing.ToDictionary(t => t.DedupeKey.Value);
        var byExt = existing.Where(t => t.ExternalRef is not null).ToDictionary(t => t.ExternalRef!.ExternalId);

        var added = new List<Transaction>();
        var enriched = new List<Transaction>();
        int updated = 0, skipped = 0;

        foreach (var e in external)
        {
            ct.ThrowIfCancellationRequested();
            if (e.Amount.Currency != account.Currency)
                _log.LogWarning("Transaction currency {Cur} differs from account {AccCur}; imported as-is", e.Amount.Currency, account.Currency);

            var normDesc = TextNormalizer.Normalize(e.Description);
            var postedDate = DateOnly.FromDateTime(e.PostedAt.UtcDateTime);
            var key = DedupeKey.Compute(account.Id, postedDate, e.Amount.Amount, e.Amount.Currency.Code, normDesc, e.ExternalId);

            Transaction? match = null;
            if (e.ExternalId is { Length: > 0 } && byExt.TryGetValue(e.ExternalId, out var m1)) match = m1;
            else if (byKey.TryGetValue(key.Value, out var m2)) match = m2;

            if (match is not null)
            {
                var changed = false;
                if (match.Status != e.Status || (match.Mcc is null && e.Mcc is not null))
                {
                    match.UpdateFromSource(e.Status, e.BookedAt, e.Mcc);
                    changed = true;
                }
                // Более полный формат той же выписки (1С после краткой XLSX): подтягиваем контрагента и назначение, пересопоставляем
                if (match.EnrichFromSource(e.Counterparty, e.Purpose, e.Description))
                {
                    enriched.Add(match);
                    changed = true;
                }
                if (changed) updated++; else skipped++;
                continue;
            }

            Guid? rawId = null;
            if (e.RawPayload is not null && connection is not null)
            {
                var raw = new RawRecord(connection.Id, source, e.RawPayload, fileName);
                await _uow.RawRecords.AddAsync(raw, ct);
                rawId = raw.Id;
            }

            var tx = new Transaction(
                account.Id, e.PostedAt, e.Amount, e.Description, source, key,
                e.Counterparty, e.Purpose, e.Mcc, e.Status,
                e.ExternalId is { Length: > 0 } ? new ExternalRef(source, e.ExternalId) : null,
                rawId, e.BookedAt);

            added.Add(tx);
            byKey[key.Value] = tx;
            if (e.ExternalId is { Length: > 0 }) byExt[e.ExternalId] = tx;
        }

        await _uow.Transactions.AddRangeAsync(added, ct);

        var cpCreated = await ResolveCounterpartiesAsync(userId, added.Concat(enriched).ToList(), ct);
        var transfers = await LinkTransfersAsync(userId, added, existing, ct);
        var categorized = CategorizeAsync(userId, added.Concat(enriched.Where(t => t.CategoryId == null)).ToList());

        await _uow.SaveChangesAsync(ct);

        _log.LogInformation("Import into {Account}: +{Added} ~{Updated} ={Skipped}, cp+{Cp}, transfers {Tr}, categorized {Cat}",
            account.Name, added.Count, updated, skipped, cpCreated, transfers, categorized);

        return new ImportSummary(added.Count, updated, skipped, cpCreated, transfers, categorized);
    }

    private async Task<int> ResolveCounterpartiesAsync(string userId, List<Transaction> txs, CancellationToken ct)
    {
        var known = _uow.Counterparties.Query().Where(c => c.UserId == userId).ToList();
        var ownAccounts = _uow.Accounts.Query().Where(a => a.UserId == userId && a.AccountNumber != null).Select(a => a.AccountNumber!).ToList();
        var ownInns = _uow.Profiles.Query().Where(p => p.UserId == userId && p.Inn != null).Select(p => p.Inn!).ToList();

        var matcher = new CounterpartyMatcher(userId, known, ownAccounts, ownInns, []);
        var knownIds = known.Select(c => c.Id).ToHashSet();
        var created = 0;

        foreach (var t in txs)
        {
            var m = matcher.Resolve(t.CounterpartyRaw);
            if (m is null) continue;
            t.ResolveCounterparty(m.Counterparty.Id);
            // Торговая точка: есть MCC или операция по бизнес-карте («Покупка: …», «Отмена покупки: …»)
            if (m.Counterparty.Kind == CounterpartyKind.Unknown && (t.Mcc is not null || t.Description.StartsWith("Покупка", StringComparison.Ordinal) || t.Description.Contains("покупки:", StringComparison.Ordinal)))
                m.Counterparty.SetKind(CounterpartyKind.Merchant);
            if (m.Created && knownIds.Add(m.Counterparty.Id))
            {
                await _uow.Counterparties.AddAsync(m.Counterparty, ct);
                created++;
            }
        }
        return created;
    }

    private async Task<int> LinkTransfersAsync(string userId, List<Transaction> added, List<Transaction> existingSameAccount, CancellationToken ct)
    {
        if (added.Count == 0) return 0;
        var min = added.Min(t => t.PostedAt).AddDays(-3);
        var max = added.Max(t => t.PostedAt).AddDays(3);

        var accountIds = _uow.Accounts.Query().Where(a => a.UserId == userId).Select(a => a.Id).ToList();
        var others = _uow.Transactions.Query()
            .Where(t => accountIds.Contains(t.AccountId) && t.PostedAt >= min && t.PostedAt <= max && t.TransferLinkId == null)
            .ToList();

        var pool = others.Concat(added).DistinctBy(t => t.Id);
        var links = new TransferMatcher().FindPairs(pool);
        await _uow.TransferLinks.AddRangeAsync(links, ct);
        return links.Count;
    }

    private int CategorizeAsync(string userId, List<Transaction> txs)
    {
        var rules = _uow.Rules.Query().Where(r => r.UserId == null || r.UserId == userId).ToList();
        var cats = _uow.Categories.Query().Where(c => c.UserId == null || c.UserId == userId).ToList();
        var codeToId = cats.Where(c => c.Code != null).ToDictionary(c => c.Code!, c => c.Id);
        var mcc = Categorization.SystemCategories.MccToCode
            .Where(kv => codeToId.ContainsKey(kv.Value))
            .ToDictionary(kv => kv.Key, kv => codeToId[kv.Value]);
        var cps = _uow.Counterparties.Query().Where(c => c.UserId == userId).ToList();

        var categorizer = new RuleCategorizer(rules, mcc, cps);
        var transferCat = codeToId.GetValueOrDefault("transfer");
        var n = 0;

        foreach (var t in txs)
        {
            if (t.Kind == TransactionKind.Transfer && transferCat != default)
            {
                if (t.Categorize(transferCat, CategorySource.Rule, 1m)) n++;
                continue;
            }
            var s = categorizer.Suggest(t);
            if (s is null) continue;
            if (t.Categorize(s.CategoryId, s.Source, s.Confidence)) n++;
        }
        return n;
    }
}
