namespace CashFlow.Domain.Ledger.Services;

/// <summary>
/// Находит пары «списание ↔ зачисление» между своими счетами.
/// Правила: разные счета, противоположные знаки, одинаковая сумма (в одной валюте), окно ±2 дня, ни одна не связана.
/// </summary>
public sealed class TransferMatcher
{
    private readonly TimeSpan _window;

    public TransferMatcher(TimeSpan? window = null) => _window = window ?? TimeSpan.FromDays(2);

    public IReadOnlyList<TransferLink> FindPairs(IEnumerable<Transaction> transactions)
    {
        var candidates = transactions
            .Where(t => t.TransferLinkId is null && t.Status != TransactionStatus.Cancelled)
            .OrderBy(t => t.PostedAt)
            .ToList();

        var links = new List<TransferLink>();
        var used = new HashSet<Guid>();

        foreach (var outgoing in candidates.Where(t => t.IsExpense))
        {
            if (used.Contains(outgoing.Id)) continue;
            var abs = outgoing.Amount.Abs();

            var incoming = candidates.FirstOrDefault(t =>
                !used.Contains(t.Id)
                && t.IsIncome
                && t.AccountId != outgoing.AccountId
                && t.Amount.Currency == abs.Currency
                && t.Amount.Amount == abs.Amount
                && (t.PostedAt - outgoing.PostedAt).Duration() <= _window
                && LooksLikeTransfer(outgoing, t));

            if (incoming is null) continue;

            var link = new TransferLink(outgoing.Id, incoming.Id, auto: true);
            outgoing.LinkTransfer(link.Id);
            incoming.LinkTransfer(link.Id);
            used.Add(outgoing.Id);
            used.Add(incoming.Id);
            links.Add(link);
        }

        return links;
    }

    private static bool LooksLikeTransfer(Transaction a, Transaction b)
    {
        // Если обе стороны помечены как Self-контрагент — точно перевод.
        // Иначе — достаточно совпадения суммы и окна; ложные срабатывания редки при равных суммах между своими счетами.
        static bool Hint(Transaction t)
        {
            var d = Shared.TextNormalizer.Normalize(t.Description + " " + t.Purpose);
            return d.Contains("перевод") || d.Contains("между своими") || d.Contains("собственных средств") || d.Contains("пополнение") || d.Contains("transfer") || d.Contains("сбп");
        }
        return Hint(a) || Hint(b);
    }
}
