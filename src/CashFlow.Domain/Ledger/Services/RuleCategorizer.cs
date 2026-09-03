namespace CashFlow.Domain.Ledger.Services;

public sealed record CategorySuggestion(Guid CategoryId, CategorySource Source, decimal Confidence, string Reason);

/// <summary>
/// Детерминированная категоризация: контрагент по умолчанию → правила пользователя/выученные → системные правила → MCC.
/// </summary>
public sealed class RuleCategorizer
{
    private readonly List<CategorizationRule> _rules;
    private readonly IReadOnlyDictionary<string, Guid> _mccToCategory;
    private readonly IReadOnlyDictionary<Guid, Counterparty> _counterparties;

    public RuleCategorizer(IEnumerable<CategorizationRule> rules, IReadOnlyDictionary<string, Guid> mccToCategory, IEnumerable<Counterparty> counterparties)
    {
        _rules = rules.Where(r => r.IsEnabled)
            .OrderByDescending(r => r.Origin == RuleOrigin.User)
            .ThenByDescending(r => r.Origin == RuleOrigin.AiLearned)
            .ThenByDescending(r => r.Priority)
            .ToList();
        _mccToCategory = mccToCategory;
        _counterparties = counterparties.ToDictionary(c => c.Id);
    }

    public CategorySuggestion? Suggest(Transaction t)
    {
        if (t.Kind == TransactionKind.Transfer) return null;

        if (t.CounterpartyId is { } cpId && _counterparties.TryGetValue(cpId, out var cp) && cp.DefaultCategoryId is { } def)
            return new CategorySuggestion(def, CategorySource.Counterparty, 0.95m, $"counterparty:{cp.DisplayName}");

        var cpName = t.CounterpartyRaw.Name ?? (t.CounterpartyId is { } id && _counterparties.TryGetValue(id, out var c) ? c.DisplayName : null);

        foreach (var rule in _rules)
        {
            var value = rule.Field switch
            {
                RuleField.Description => t.Description + " " + t.Purpose,
                RuleField.CounterpartyName => cpName,
                RuleField.CounterpartyInn => t.CounterpartyRaw.Inn,
                RuleField.Mcc => t.Mcc,
                _ => null,
            };
            if (rule.Matches(value))
            {
                rule.Hit();
                var conf = rule.Origin switch { RuleOrigin.User => 1m, RuleOrigin.AiLearned => 0.9m, _ => 0.8m };
                return new CategorySuggestion(rule.CategoryId, CategorySource.Rule, conf, $"rule:{rule.Field}:{rule.Pattern}");
            }
        }

        if (t.Mcc is { Length: 4 } mcc && _mccToCategory.TryGetValue(mcc, out var mccCat))
            return new CategorySuggestion(mccCat, CategorySource.Mcc, 0.75m, $"mcc:{mcc}");

        return null;
    }
}
