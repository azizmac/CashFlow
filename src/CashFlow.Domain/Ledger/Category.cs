using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Ledger;

public enum CategoryKind { Expense = 0, Income = 1, Transfer = 2, Investment = 3 }

public sealed class Category : Entity
{
    private Category() { }

    public Category(string? userId, string name, CategoryKind kind, Guid? parentId = null, string? icon = null, string? color = null, bool isSystem = false, string? code = null)
    {
        UserId = userId;
        Name = name;
        Kind = kind;
        ParentId = parentId;
        Icon = icon;
        Color = color;
        IsSystem = isSystem;
        Code = code;
    }

    /// <summary>null — системная категория, общая для всех.</summary>
    public string? UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public CategoryKind Kind { get; private set; }
    public Guid? ParentId { get; private set; }
    public string? Icon { get; private set; }
    public string? Color { get; private set; }
    public bool IsSystem { get; private set; }
    /// <summary>Стабильный код системной категории (groceries, transport…).</summary>
    public string? Code { get; private set; }

    public void Rename(string name) { Name = name; Touch(); }
    public void SetAppearance(string? icon, string? color) { Icon = icon; Color = color; Touch(); }
    public void MoveTo(Guid? parentId) { ParentId = parentId; Touch(); }
}

public enum RuleOrigin { System = 0, User = 1, AiLearned = 2 }
public enum RuleField { Description = 0, CounterpartyName = 1, CounterpartyInn = 2, Mcc = 3 }
public enum RuleMatch { Contains = 0, Equals = 1, StartsWith = 2, Regex = 3 }

/// <summary>Правило категоризации. Применяется по убыванию Priority.</summary>
public sealed class CategorizationRule : Entity
{
    private CategorizationRule() { }

    public CategorizationRule(string? userId, RuleField field, RuleMatch match, string pattern, Guid categoryId, int priority, RuleOrigin origin)
    {
        UserId = userId;
        Field = field;
        Match = match;
        Pattern = pattern;
        CategoryId = categoryId;
        Priority = priority;
        Origin = origin;
        IsEnabled = true;
    }

    public string? UserId { get; private set; }
    public RuleField Field { get; private set; }
    public RuleMatch Match { get; private set; }
    public string Pattern { get; private set; } = default!;
    public Guid CategoryId { get; private set; }
    public int Priority { get; private set; }
    public RuleOrigin Origin { get; private set; }
    public bool IsEnabled { get; private set; }
    public int HitCount { get; private set; }

    public void Hit() { HitCount++; }
    public void Enable(bool v) { IsEnabled = v; Touch(); }

    public bool Matches(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var v = TextNormalizer.Normalize(value);
        var p = Match == RuleMatch.Regex ? Pattern : TextNormalizer.Normalize(Pattern);
        return Match switch
        {
            RuleMatch.Contains => v.Contains(p, StringComparison.Ordinal),
            RuleMatch.Equals => v == p,
            RuleMatch.StartsWith => v.StartsWith(p, StringComparison.Ordinal),
            RuleMatch.Regex => System.Text.RegularExpressions.Regex.IsMatch(v, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
            _ => false,
        };
    }
}
