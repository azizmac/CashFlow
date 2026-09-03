using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Ledger;

public enum CounterpartyKind
{
    Unknown = 0,
    Person = 1,
    Company = 2,
    SoleProprietor = 3,
    Bank = 4,
    Government = 5,
    Merchant = 6,
    /// <summary>Сам пользователь (его другой счёт).</summary>
    Self = 7,
}

/// <summary>Контрагент: кто прислал / кому ушли деньги. Все написания из выписок — в Aliases.</summary>
public sealed class Counterparty : Entity
{
    private readonly List<string> _aliases = [];
    private readonly List<string> _accounts = [];
    private readonly List<string> _phones = [];

    private Counterparty() { }

    public Counterparty(string userId, string displayName, CounterpartyKind kind)
    {
        UserId = userId;
        DisplayName = displayName;
        Kind = kind;
        AddAlias(displayName);
    }

    public string UserId { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public CounterpartyKind Kind { get; private set; }
    /// <summary>Шифруется.</summary>
    public string? Inn { get; private set; }
    public string? Kpp { get; private set; }
    public Guid? DefaultCategoryId { get; private set; }
    public bool IsFavorite { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Нормализованные варианты имени (TextNormalizer.Normalize).</summary>
    public IReadOnlyList<string> Aliases => _aliases;
    /// <summary>Номера счетов контрагента. Шифруются.</summary>
    public IReadOnlyList<string> Accounts => _accounts;
    /// <summary>Телефоны (СБП). Шифруются.</summary>
    public IReadOnlyList<string> Phones => _phones;

    public void Rename(string displayName) { DisplayName = displayName; AddAlias(displayName); Touch(); }
    public void SetKind(CounterpartyKind kind) { Kind = kind; Touch(); }
    public void SetInn(string? inn, string? kpp = null) { Inn = inn; Kpp = kpp ?? Kpp; Touch(); }
    public void SetDefaultCategory(Guid? categoryId) { DefaultCategoryId = categoryId; Touch(); }
    public void SetFavorite(bool v) { IsFavorite = v; Touch(); }
    public void SetNote(string? note) { Note = note; Touch(); }

    public bool AddAlias(string rawName)
    {
        var n = TextNormalizer.Normalize(rawName);
        if (n.Length == 0 || _aliases.Contains(n)) return false;
        _aliases.Add(n);
        Touch();
        return true;
    }

    public bool AddAccount(string account)
    {
        var a = account.Trim();
        if (a.Length == 0 || _accounts.Contains(a)) return false;
        _accounts.Add(a);
        Touch();
        return true;
    }

    public bool AddPhone(string phone)
    {
        var p = NormalizePhone(phone);
        if (p.Length == 0 || _phones.Contains(p)) return false;
        _phones.Add(p);
        Touch();
        return true;
    }

    /// <summary>Слить другого контрагента в этого: алиасы, счета, телефоны переносятся.</summary>
    public void Absorb(Counterparty other)
    {
        foreach (var a in other._aliases) if (!_aliases.Contains(a)) _aliases.Add(a);
        foreach (var a in other._accounts) if (!_accounts.Contains(a)) _accounts.Add(a);
        foreach (var p in other._phones) if (!_phones.Contains(p)) _phones.Add(p);
        Inn ??= other.Inn;
        Kpp ??= other.Kpp;
        DefaultCategoryId ??= other.DefaultCategoryId;
        Touch();
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        if (digits.Length == 10) digits = "7" + digits;
        return digits.Length == 11 ? "+" + digits : string.Empty;
    }

    /// <summary>Определяет тип по ИНН: 10 цифр — юрлицо, 12 — физлицо/ИП.</summary>
    public static CounterpartyKind KindFromInn(string? inn, bool hintSoleProprietor = false) => inn?.Length switch
    {
        10 => CounterpartyKind.Company,
        12 => hintSoleProprietor ? CounterpartyKind.SoleProprietor : CounterpartyKind.Person,
        _ => CounterpartyKind.Unknown,
    };
}

/// <summary>Реквизиты контрагента как пришли из источника. Value object на транзакции.</summary>
public sealed record CounterpartyRaw(
    string? Name,
    string? Inn = null,
    string? Kpp = null,
    string? Account = null,
    string? Bik = null,
    string? BankName = null,
    string? Phone = null)
{
    public static readonly CounterpartyRaw Empty = new(Name: null);
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && Inn is null && Account is null && Phone is null;
}
