using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;
using AccountType = CashFlow.Domain.Ledger.AccountType;
using Currency = CashFlow.Domain.Shared.Currency;

namespace CashFlow.Connectors.TInvest;

/// <summary>
/// T-Invest API (gRPC) — только чтение. Требуется токен с правами «только чтение».
/// Секреты: token.
/// </summary>
public sealed class TInvestConnector : ReadOnlyConnectorBase
{
    public const string SecretToken = "token";
    private readonly ILogger<TInvestConnector> _log;

    public TInvestConnector(ILogger<TInvestConnector> log) => _log = log;

    public override ConnectorType Type => ConnectorType.TInvest;
    public override ConnectorCapabilities Capabilities => ConnectorCapabilities.Accounts | ConnectorCapabilities.Balances | ConnectorCapabilities.Transactions | ConnectorCapabilities.Positions;
    public override IReadOnlyList<string> RequiredSecrets => [SecretToken];

    private static InvestApiClient Client(ConnectionContext ctx) =>
        InvestApiClientFactory.Create(new InvestApiSettings { AccessToken = ctx.Secret(SecretToken), AppName = "cashflow-ai", Sandbox = false });

    public override async Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var client = Client(ctx);
        var accounts = await client.Users.GetAccountsAsync(new GetAccountsRequest { Status = AccountStatus.Open }, cancellationToken: ct);
        var result = new List<ExternalAccount>();

        foreach (var a in accounts.Accounts)
        {
            if (a.AccessLevel == AccessLevel.AccountAccessLevelNoAccess) continue;
            Money? total = null;
            try
            {
                var pf = await client.Operations.GetPortfolioAsync(new PortfolioRequest { AccountId = a.Id, Currency = PortfolioRequest.Types.CurrencyRequest.Rub }, cancellationToken: ct);
                total = ToMoney(pf.TotalAmountPortfolio);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.PermissionDenied || ex.StatusCode == Grpc.Core.StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("T-Invest: токен недействителен или недостаточно прав", ex);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "T-Invest: portfolio for {Account} unavailable", a.Id);
            }

            var type = a.Type == Tinkoff.InvestApi.V1.AccountType.TinkoffIis ? AccountType.Iis : AccountType.Brokerage;
            result.Add(new ExternalAccount(a.Id, string.IsNullOrWhiteSpace(a.Name) ? $"Т-Инвестиции {type}" : a.Name, type, Currency.RUB, null, total));
        }
        return result;
    }

    public override async Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct)
    {
        var client = Client(ctx);
        var list = new List<ExternalTransaction>();
        string? cursor = null;

        do
        {
            var req = new GetOperationsByCursorRequest
            {
                AccountId = accountExternalId,
                From = Timestamp.FromDateTime(range.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                To = Timestamp.FromDateTime(range.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc)),
                State = OperationState.Executed,
                Limit = 1000,
                WithoutTrades = true,
            };
            if (cursor is not null) req.Cursor = cursor;

            GetOperationsByCursorResponse resp;
            try { resp = await client.Operations.GetOperationsByCursorAsync(req, cancellationToken: ct); }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode is Grpc.Core.StatusCode.PermissionDenied or Grpc.Core.StatusCode.Unauthenticated)
            { throw new UnauthorizedAccessException("T-Invest: токен недействителен", ex); }

            foreach (var op in resp.Items)
            {
                var payment = ToMoney(op.Payment);
                if (payment is null || payment.Amount == 0) continue; // сделки без движения денег (переводы бумаг) не влияют на cash flow

                var kindDesc = string.IsNullOrWhiteSpace(op.Description) ? op.Name : op.Description;
                var desc = string.IsNullOrWhiteSpace(op.Ticker) ? kindDesc : $"{kindDesc} {op.Ticker}";
                var cp = new CounterpartyRaw(MapCounterparty(op.Type, op.Ticker, op.Name));

                list.Add(new ExternalTransaction(
                    op.Id, accountExternalId, op.Date.ToDateTimeOffset(), payment, desc, cp,
                    Purpose: op.Type.ToString(), Status: TransactionStatus.Posted,
                    RawPayload: Google.Protobuf.JsonFormatter.Default.Format(op)));
            }

            cursor = resp.HasNext ? resp.NextCursor : null;
        } while (cursor is not null);

        return list;
    }

    public override async Task<IReadOnlyList<ExternalPosition>> GetPositionsAsync(ConnectionContext ctx, string accountExternalId, CancellationToken ct)
    {
        var client = Client(ctx);
        var pf = await client.Operations.GetPortfolioAsync(new PortfolioRequest { AccountId = accountExternalId, Currency = PortfolioRequest.Types.CurrencyRequest.Rub }, cancellationToken: ct);
        var res = new List<ExternalPosition>();
        foreach (var p in pf.Positions)
        {
            string name = p.Ticker, isin = "";
            try
            {
                var ins = await client.Instruments.GetInstrumentByAsync(new InstrumentRequest { IdType = InstrumentIdType.Uid, Id = p.InstrumentUid }, cancellationToken: ct);
                name = ins.Instrument.Name; isin = ins.Instrument.Isin;
            }
            catch (Exception ex) { _log.LogDebug(ex, "instrument {Uid} lookup failed", p.InstrumentUid); }

            res.Add(new ExternalPosition(accountExternalId, p.InstrumentUid, p.Ticker, isin, name, p.InstrumentType,
                ToDecimal(p.Quantity), ToMoney(p.AveragePositionPrice), ToMoney(p.CurrentPrice)));
        }
        return res;
    }

    private static string MapCounterparty(OperationType type, string ticker, string name) => type switch
    {
        OperationType.Dividend or OperationType.Coupon or OperationType.DividendTransfer or OperationType.BondRepayment or OperationType.BondRepaymentFull or OperationType.DivExt
            => string.IsNullOrWhiteSpace(ticker) ? "Эмитент" : $"Эмитент {ticker}",
        OperationType.BrokerFee or OperationType.ServiceFee or OperationType.MarginFee or OperationType.SuccessFee or OperationType.CashFee or OperationType.OutFee or OperationType.AdviceFee or OperationType.TrackMfee or OperationType.TrackPfee or OperationType.OtherFee
            => "Т-Инвестиции (комиссия)",
        OperationType.Tax or OperationType.BondTax or OperationType.DividendTax or OperationType.BenefitTax or OperationType.TaxCorrection or OperationType.TaxProgressive or OperationType.BondTaxProgressive or OperationType.DividendTaxProgressive or OperationType.TaxRepo or OperationType.TaxRepoHold or OperationType.TaxRepoRefund or OperationType.TaxCorrectionCoupon
            => "ФНС (налог, брокер-агент)",
        OperationType.Input or OperationType.Output or OperationType.InputSwift or OperationType.OutputSwift or OperationType.InputAcquiring or OperationType.OutputAcquiring or OperationType.TransIisBs or OperationType.TransBsBs or OperationType.OutMulti or OperationType.InpMulti
            => "Я (свои счета)",
        OperationType.Buy or OperationType.Sell or OperationType.BuyCard or OperationType.SellCard or OperationType.BuyMargin or OperationType.SellMargin
            => string.IsNullOrWhiteSpace(ticker) ? "Биржа" : $"Биржа {ticker}",
        _ => string.IsNullOrWhiteSpace(name) ? "Т-Инвестиции" : name,
    };

    internal static Money? ToMoney(MoneyValue? m) => m is null || string.IsNullOrEmpty(m.Currency) ? null
        : new Money(m.Units + m.Nano / 1_000_000_000m, Currency.FromStatement(m.Currency));

    internal static decimal ToDecimal(Quotation? q) => q is null ? 0m : q.Units + q.Nano / 1_000_000_000m;
}
