using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Connections;

public enum InstitutionKind { Bank = 0, Broker = 1, Wallet = 2, Cash = 3, Manual = 4 }

/// <summary>Банк / брокер. Справочник.</summary>
public sealed class Institution : Entity
{
    private Institution() { }

    public Institution(string code, string name, InstitutionKind kind, string? bic = null)
    {
        Code = code;
        Name = name;
        Kind = kind;
        Bic = bic;
    }

    /// <summary>Короткий стабильный код: sber, tbank, cash…</summary>
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public InstitutionKind Kind { get; private set; }
    public string? Bic { get; private set; }

    public static class Codes
    {
        public const string Sber = "sber";
        public const string TBank = "tbank";
        public const string TInvest = "tinvest";
        public const string Vtb = "vtb";
        public const string Alfa = "alfa";
        public const string Cash = "cash";
        public const string Other = "other";
    }
}
