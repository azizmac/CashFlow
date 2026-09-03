using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Categorization;

/// <summary>Ручная категоризация пользователем + обучение правил на исправлениях.</summary>
public sealed class CategorizationService
{
    private readonly IUnitOfWork _uow;
    private const int LearnAfterConfirmations = 2;

    public CategorizationService(IUnitOfWork uow) => _uow = uow;

    public async Task SetCategoryAsync(string userId, Guid transactionId, Guid? categoryId, bool applyToCounterparty, CancellationToken ct)
    {
        var tx = await _uow.Transactions.FindAsync(transactionId, ct) ?? throw new InvalidOperationException("Transaction not found");
        var account = await _uow.Accounts.FindAsync(tx.AccountId, ct);
        if (account?.UserId != userId) throw new UnauthorizedAccessException();

        tx.SetCategoryByUser(categoryId);

        if (categoryId is { } cat && tx.CounterpartyId is { } cpId)
        {
            var cp = await _uow.Counterparties.FindAsync(cpId, ct);
            if (cp is not null)
            {
                if (applyToCounterparty)
                {
                    cp.SetDefaultCategory(cat);
                    // Перекатегоризировать некатегоризированные/автоматические операции этого контрагента
                    var others = _uow.Transactions.Query()
                        .Where(t => t.CounterpartyId == cpId && t.Id != tx.Id && !t.ReviewedByUser)
                        .ToList();
                    foreach (var o in others) o.Categorize(cat, CategorySource.Counterparty, 0.95m);
                }
                else
                {
                    // Учимся: N подтверждений одной категории для контрагента → правило AiLearned
                    var confirmations = _uow.Transactions.Query()
                        .Count(t => t.CounterpartyId == cpId && t.ReviewedByUser && t.CategoryId == cat);
                    if (confirmations >= LearnAfterConfirmations && cp.DefaultCategoryId is null)
                        cp.SetDefaultCategory(cat);
                }
            }
        }
        else if (categoryId is { } cat2 && tx.CounterpartyId is null)
        {
            // Нет контрагента — учимся на нормализованном описании
            var norm = TextNormalizer.Normalize(tx.Description);
            if (norm.Length >= 5)
            {
                var same = _uow.Transactions.Query()
                    .Where(t => t.ReviewedByUser && t.CategoryId == cat2)
                    .Select(t => t.Description).ToList()
                    .Count(d => TextNormalizer.Normalize(d) == norm);
                var exists = _uow.Rules.Query().Any(r => r.UserId == userId && r.Field == RuleField.Description && r.Pattern == norm);
                if (same >= LearnAfterConfirmations && !exists)
                    await _uow.Rules.AddAsync(new CategorizationRule(userId, RuleField.Description, RuleMatch.Equals, norm, cat2, 100, RuleOrigin.AiLearned), ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task AcceptProposalAsync(string userId, Guid transactionId, CancellationToken ct)
    {
        var tx = await _uow.Transactions.FindAsync(transactionId, ct) ?? throw new InvalidOperationException("Transaction not found");
        var account = await _uow.Accounts.FindAsync(tx.AccountId, ct);
        if (account?.UserId != userId) throw new UnauthorizedAccessException();
        tx.AcceptProposal();
        await _uow.SaveChangesAsync(ct);
    }
}
