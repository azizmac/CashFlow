using CashFlow.Application.Categorization;
using CashFlow.Application.Contracts;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Ledger;

/// <summary>Изменения, которые делает пользователь руками. Каждая команда проверяет владельца.</summary>
public sealed class LedgerCommands : ILedgerCommands
{
    private readonly IUnitOfWork _uow;
    private readonly CategorizationService _categorization;

    public LedgerCommands(IUnitOfWork uow, CategorizationService categorization)
    {
        _uow = uow;
        _categorization = categorization;
    }

    public Task SetCategoryAsync(string userId, Guid transactionId, Guid? categoryId, bool applyToCounterparty, CancellationToken ct = default) =>
        _categorization.SetCategoryAsync(userId, transactionId, categoryId, applyToCounterparty, ct);

    public Task AcceptProposalAsync(string userId, Guid transactionId, CancellationToken ct = default) =>
        _categorization.AcceptProposalAsync(userId, transactionId, ct);

    public async Task SetNoteAsync(string userId, Guid transactionId, string? note, IEnumerable<string> tags, CancellationToken ct = default)
    {
        var tx = await OwnTransactionAsync(userId, transactionId, ct);
        tx.SetNote(string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        tx.SetTags(tags);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task SetAccountFlagsAsync(string userId, Guid accountId, bool includeInCashFlow, bool includeInNetWorth, CancellationToken ct = default)
    {
        var a = await OwnAccountAsync(userId, accountId, ct);
        a.SetFlags(includeInNetWorth, includeInCashFlow);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ArchiveAccountAsync(string userId, Guid accountId, CancellationToken ct = default)
    {
        var a = await OwnAccountAsync(userId, accountId, ct);
        a.Archive();
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<AccountDto> CreateManualAccountAsync(string userId, Guid profileId, AccountType type, string name, string currency, decimal balance, CancellationToken ct = default)
    {
        var profile = await _uow.Profiles.FindAsync(profileId, ct);
        if (profile is null || profile.UserId != userId) throw new UnauthorizedAccessException();
        var instCode = type == AccountType.Cash ? Institution.Codes.Cash : Institution.Codes.Other;
        var inst = _uow.Institutions.Query().First(i => i.Code == instCode);
        var a = new Account(userId, profileId, inst.Id, type, name.Trim(), Currency.Parse(currency));
        await _uow.Accounts.AddAsync(a, ct);
        await _uow.BalanceSnapshots.AddAsync(a.RecordBalance(new Money(balance, a.Currency)), ct);
        await _uow.SaveChangesAsync(ct);
        return a.ToDto(inst, profile);
    }

    public async Task RenameCounterpartyAsync(string userId, Guid counterpartyId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var c = await OwnCounterpartyAsync(userId, counterpartyId, ct);
        c.Rename(name.Trim());
        await _uow.SaveChangesAsync(ct);
    }

    public async Task SetCounterpartyKindAsync(string userId, Guid counterpartyId, CounterpartyKind kind, CancellationToken ct = default)
    {
        var c = await OwnCounterpartyAsync(userId, counterpartyId, ct);
        c.SetKind(kind);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task SetCounterpartyDefaultCategoryAsync(string userId, Guid counterpartyId, Guid? categoryId, CancellationToken ct = default)
    {
        var c = await OwnCounterpartyAsync(userId, counterpartyId, ct);
        c.SetDefaultCategory(categoryId);
        if (categoryId is { } cat)
        {
            // Категория по умолчанию применяется ко всем ещё не разобранным вручную операциям контрагента
            var txs = _uow.Transactions.Query().Where(t => t.CounterpartyId == c.Id && !t.ReviewedByUser).ToList();
            foreach (var t in txs) t.Categorize(cat, CategorySource.Counterparty, 0.95m);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Transaction> OwnTransactionAsync(string userId, Guid id, CancellationToken ct)
    {
        var tx = await _uow.Transactions.FindAsync(id, ct) ?? throw new KeyNotFoundException("Операция не найдена");
        var account = await _uow.Accounts.FindAsync(tx.AccountId, ct);
        if (account?.UserId != userId) throw new UnauthorizedAccessException();
        return tx;
    }

    private async Task<Account> OwnAccountAsync(string userId, Guid id, CancellationToken ct)
    {
        var a = await _uow.Accounts.FindAsync(id, ct);
        if (a is null || a.UserId != userId) throw new UnauthorizedAccessException();
        return a;
    }

    private async Task<Counterparty> OwnCounterpartyAsync(string userId, Guid id, CancellationToken ct)
    {
        var c = await _uow.Counterparties.FindAsync(id, ct);
        if (c is null || c.UserId != userId) throw new UnauthorizedAccessException();
        return c;
    }
}
