using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Ledger;

public enum AccountType
{
    Checking = 0,     // расчётный / текущий счёт
    Card = 1,         // карточный счёт физлица
    Savings = 2,      // накопительный
    Deposit = 3,      // вклад
    CreditCard = 4,
    Loan = 5,
    Brokerage = 6,
    Iis = 7,
    Cash = 8,
    EWallet = 9,
}

public sealed class Account : Entity
{
    private Account() { }

    public Account(string userId, Guid profileId, Guid institutionId, AccountType type, string name, Currency currency,
        Guid? connectionId = null, ExternalRef? externalRef = null, string? accountNumber = null)
    {
        UserId = userId;
        ProfileId = profileId;
        InstitutionId = institutionId;
        Type = type;
        Name = name;
        Currency = currency;
        ConnectionId = connectionId;
        ExternalRef = externalRef;
        AccountNumber = accountNumber;
        IncludeInNetWorth = true;
        IncludeInCashFlow = type is not (AccountType.Brokerage or AccountType.Iis);
    }

    public string UserId { get; private set; } = default!;
    public Guid ProfileId { get; private set; }
    public Guid InstitutionId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public AccountType Type { get; private set; }
    public string Name { get; private set; } = default!;
    public Currency Currency { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }
    /// <summary>Номер счёта / маскированный номер карты. Шифруется.</summary>
    public string? AccountNumber { get; private set; }
    public bool IsArchived { get; private set; }
    public bool IncludeInNetWorth { get; private set; }
    public bool IncludeInCashFlow { get; private set; }

    public Money? LastBalance { get; private set; }
    public DateTimeOffset? LastBalanceAt { get; private set; }

    public void Rename(string name) { Name = name; Touch(); }
    public void Archive() { IsArchived = true; Touch(); }
    public void SetFlags(bool netWorth, bool cashFlow) { IncludeInNetWorth = netWorth; IncludeInCashFlow = cashFlow; Touch(); }

    public BalanceSnapshot RecordBalance(Money current, Money? available = null, Money? blocked = null, DateTimeOffset? at = null)
    {
        if (current.Currency != Currency) throw new InvalidOperationException("Balance currency mismatch");
        var ts = at ?? DateTimeOffset.UtcNow;
        LastBalance = current;
        LastBalanceAt = ts;
        Touch();
        Raise(new BalanceSnapshotTaken(Id, current, ts));
        return new BalanceSnapshot(Id, ts, current, available, blocked);
    }
}

public sealed class BalanceSnapshot : Entity
{
    private BalanceSnapshot() { }

    public BalanceSnapshot(Guid accountId, DateTimeOffset at, Money current, Money? available, Money? blocked)
    {
        AccountId = accountId;
        At = at;
        Current = current;
        Available = available;
        Blocked = blocked;
    }

    public Guid AccountId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Money Current { get; private set; }
    public Money? Available { get; private set; }
    public Money? Blocked { get; private set; }
}

public sealed record BalanceSnapshotTaken(Guid AccountId, Money Balance, DateTimeOffset At) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
