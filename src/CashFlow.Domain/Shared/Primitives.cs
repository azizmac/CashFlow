using System.Security.Cryptography;
using System.Text;

namespace CashFlow.Domain.Shared;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();
    protected void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly d) => d >= From && d <= To;
    public static DateRange LastDays(int days, DateOnly? today = null)
    {
        var t = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return new DateRange(t.AddDays(-days), t);
    }
}

/// <summary>Тип источника данных.</summary>
public enum ConnectorType
{
    Manual = 0,
    StatementImport = 1,
    TInvest = 10,
    TBankBusiness = 11,
    SberBusiness = 20,
    AlfaBusiness = 21,
    CbrOpenApi = 30,
    PushListener = 40,
}

/// <summary>Ссылка на объект во внешней системе.</summary>
public sealed record ExternalRef(ConnectorType Connector, string ExternalId)
{
    public override string ToString() => $"{Connector}:{ExternalId}";
}

/// <summary>Ключ дедупликации операции. Стабилен между повторными импортами.</summary>
public readonly record struct DedupeKey(string Value)
{
    public static DedupeKey Compute(Guid accountId, DateOnly postedDate, decimal amount, string currency, string normalizedDescription, string? externalId)
    {
        // Если есть внешний ID — он главный. Иначе — содержательные поля.
        var material = externalId is { Length: > 0 }
            ? $"{accountId:N}|ext|{externalId}"
            : $"{accountId:N}|{postedDate:yyyy-MM-dd}|{amount:0.00}|{currency}|{normalizedDescription}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new DedupeKey(Convert.ToHexStringLower(hash));
    }

    public override string ToString() => Value;
}

public static class TextNormalizer
{
    /// <summary>Нормализация описания для дедупа и матчинга: нижний регистр, схлопнутые пробелы, без кавычек и пунктуации по краям.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (ch is '"' or '«' or '»' or '\'' or '`') continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
                continue;
            }
            prevSpace = false;
            sb.Append(ch == 'ё' ? 'е' : ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Убирает организационно-правовые формы для сравнения имён контрагентов.</summary>
    public static string StripLegalForm(string normalizedName)
    {
        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t is not ("ооо" or "ао" or "пао" or "зао" or "оао" or "ип" or "нко" or "llc" or "ltd" or "inc" or "jsc"));
        return string.Join(' ', tokens);
    }
}
