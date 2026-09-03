namespace CashFlow.Domain.Shared;

/// <summary>Денежная сумма с валютой. Знак = направление (минус — расход).</summary>
public sealed record Money(decimal Amount, Currency Currency)
{
    public static Money Zero(Currency c) => new(0m, c);

    public bool IsNegative => Amount < 0;
    public bool IsPositive => Amount > 0;
    public Money Abs() => this with { Amount = Math.Abs(Amount) };
    public Money Negate() => this with { Amount = -Amount };

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a with { Amount = a.Amount + b.Amount };
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a with { Amount = a.Amount - b.Amount };
    }

    public static Money operator *(Money a, decimal k) => a with { Amount = a.Amount * k };

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}");
    }

    public override string ToString() => $"{Amount:N2} {Currency.Code}";
}

/// <summary>ISO-4217 код валюты.</summary>
public readonly record struct Currency(string Code)
{
    public static readonly Currency RUB = new("RUB");
    public static readonly Currency USD = new("USD");
    public static readonly Currency EUR = new("EUR");
    public static readonly Currency CNY = new("CNY");

    public static Currency Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            throw new ArgumentException($"Invalid currency code '{code}'", nameof(code));
        return new Currency(code.ToUpperInvariant());
    }

    /// <summary>Парсит символы и коды из выписок: ₽, руб, RUR → RUB.</summary>
    public static Currency FromStatement(string raw)
    {
        var s = raw.Trim().ToUpperInvariant();
        return s switch
        {
            "₽" or "РУБ" or "РУБ." or "RUR" or "RUB" or "643" => RUB,
            "$" or "USD" or "840" => USD,
            "€" or "EUR" or "978" => EUR,
            "¥" or "CNY" or "156" => CNY,
            _ when s.Length == 3 => new Currency(s),
            _ => throw new ArgumentException($"Unknown currency '{raw}'")
        };
    }

    public override string ToString() => Code;
}
