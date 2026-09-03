using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Investments;

public enum InstrumentType { Share = 0, Bond = 1, Etf = 2, Currency = 3, Futures = 4, Other = 9 }

public sealed class Instrument : Entity
{
    private Instrument() { }

    public Instrument(string ticker, string? isin, string? figi, string name, InstrumentType type, Currency currency, int lotSize = 1)
    {
        Ticker = ticker;
        Isin = isin;
        Figi = figi;
        Name = name;
        Type = type;
        Currency = currency;
        LotSize = lotSize;
    }

    public string Ticker { get; private set; } = default!;
    public string? Isin { get; private set; }
    /// <summary>Идентификатор T-Invest.</summary>
    public string? Figi { get; private set; }
    public string? Uid { get; private set; }
    public string Name { get; private set; } = default!;
    public InstrumentType Type { get; private set; }
    public Currency Currency { get; private set; }
    public int LotSize { get; private set; }

    public void SetUid(string uid) { Uid = uid; Touch(); }
}

public sealed class Position : Entity
{
    private Position() { }

    public Position(Guid accountId, Guid instrumentId)
    {
        AccountId = accountId;
        InstrumentId = instrumentId;
    }

    public Guid AccountId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public decimal Quantity { get; private set; }
    public Money? AveragePrice { get; private set; }
    public Money? CurrentPrice { get; private set; }
    public Money? MarketValue { get; private set; }
    public Money? UnrealizedPnl { get; private set; }

    public void Update(decimal quantity, Money? avgPrice, Money? currentPrice)
    {
        Quantity = quantity;
        AveragePrice = avgPrice;
        CurrentPrice = currentPrice;
        if (currentPrice is { } cp)
        {
            MarketValue = cp * quantity;
            if (avgPrice is { } ap) UnrealizedPnl = (cp - ap) * quantity;
        }
        Touch();
    }
}

public enum InvestmentOperationType
{
    Buy = 0, Sell = 1, Dividend = 2, Coupon = 3, Amortization = 4, Fee = 5, Tax = 6, Deposit = 7, Withdrawal = 8, Other = 99,
}

/// <summary>Операция по брокерскому счёту. Денежные типы порождают Transaction в Ledger.</summary>
public sealed class InvestmentOperation : Entity
{
    private InvestmentOperation() { }

    public InvestmentOperation(Guid accountId, Guid? instrumentId, InvestmentOperationType type, DateTimeOffset at, Money amount, decimal quantity, Money? price, ExternalRef externalRef, string? description)
    {
        AccountId = accountId;
        InstrumentId = instrumentId;
        Type = type;
        At = at;
        Amount = amount;
        Quantity = quantity;
        Price = price;
        ExternalRef = externalRef;
        Description = description;
    }

    public Guid AccountId { get; private set; }
    public Guid? InstrumentId { get; private set; }
    public InvestmentOperationType Type { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Money Amount { get; private set; }
    public decimal Quantity { get; private set; }
    public Money? Price { get; private set; }
    public ExternalRef ExternalRef { get; private set; }
    public string? Description { get; private set; }
    public Guid? LedgerTransactionId { get; private set; }

    public bool ProducesCashFlow => Type is InvestmentOperationType.Dividend or InvestmentOperationType.Coupon
        or InvestmentOperationType.Fee or InvestmentOperationType.Tax or InvestmentOperationType.Deposit or InvestmentOperationType.Withdrawal
        or InvestmentOperationType.Amortization;

    public void LinkLedger(Guid transactionId) { LedgerTransactionId = transactionId; Touch(); }
}
