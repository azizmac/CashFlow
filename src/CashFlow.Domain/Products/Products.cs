using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Products;

public enum Capitalization { None = 0, Monthly = 1, AtMaturity = 2 }

public sealed class Deposit : Entity
{
    private Deposit() { }

    public Deposit(Guid accountId, decimal ratePercent, DateOnly openedAt, DateOnly? maturityAt, Capitalization capitalization, bool replenishable)
    {
        AccountId = accountId;
        RatePercent = ratePercent;
        OpenedAt = openedAt;
        MaturityAt = maturityAt;
        Capitalization = capitalization;
        Replenishable = replenishable;
    }

    public Guid AccountId { get; private set; }
    public decimal RatePercent { get; private set; }
    public DateOnly OpenedAt { get; private set; }
    public DateOnly? MaturityAt { get; private set; }
    public Capitalization Capitalization { get; private set; }
    public bool Replenishable { get; private set; }
}

public sealed class Loan : Entity
{
    private Loan() { }

    public Loan(Guid accountId, Money principal, decimal ratePercent, DateOnly issuedAt, DateOnly maturityAt, int paymentDay, Money? monthlyPayment)
    {
        AccountId = accountId;
        Principal = principal;
        RatePercent = ratePercent;
        IssuedAt = issuedAt;
        MaturityAt = maturityAt;
        PaymentDay = paymentDay;
        MonthlyPayment = monthlyPayment;
    }

    public Guid AccountId { get; private set; }
    public Money Principal { get; private set; }
    public decimal RatePercent { get; private set; }
    public DateOnly IssuedAt { get; private set; }
    public DateOnly MaturityAt { get; private set; }
    public int PaymentDay { get; private set; }
    public Money? MonthlyPayment { get; private set; }
    public Money? OutstandingDebt { get; private set; }

    public void UpdateDebt(Money debt) { OutstandingDebt = debt; Touch(); }
}

public sealed class CreditCard : Entity
{
    private CreditCard() { }

    public CreditCard(Guid accountId, Money creditLimit, int graceDays, int statementDay)
    {
        AccountId = accountId;
        CreditLimit = creditLimit;
        GraceDays = graceDays;
        StatementDay = statementDay;
    }

    public Guid AccountId { get; private set; }
    public Money CreditLimit { get; private set; }
    public int GraceDays { get; private set; }
    public int StatementDay { get; private set; }
    public Money? MinPayment { get; private set; }
    public Money? Debt { get; private set; }

    public void Update(Money? debt, Money? minPayment) { Debt = debt; MinPayment = minPayment; Touch(); }
}
