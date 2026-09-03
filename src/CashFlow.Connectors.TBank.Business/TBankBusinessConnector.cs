using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CashFlow.Connectors.TBank.Business;

/// <summary>
/// T-API (Т-Бизнес): расчётные счета ИП/ЮЛ. Только чтение: bank-accounts + statement.
/// Токен выпускается в ЛК Т-Бизнеса с правами только на «Счета и выписки».
/// Секреты: token. Base: https://business.tbank.ru/openapi
/// </summary>
public sealed class TBankBusinessConnector : ReadOnlyConnectorBase
{
    public const string SecretToken = "token";
    public const string HttpClientName = "tbank-business";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<TBankBusinessConnector> _log;

    public TBankBusinessConnector(IHttpClientFactory http, ILogger<TBankBusinessConnector> log)
    {
        _http = http;
        _log = log;
    }

    public override ConnectorType Type => ConnectorType.TBankBusiness;
    public override ConnectorCapabilities Capabilities => ConnectorCapabilities.Accounts | ConnectorCapabilities.Balances | ConnectorCapabilities.Transactions;
    public override IReadOnlyList<string> RequiredSecrets => [SecretToken];

    private HttpClient Client(ConnectionContext ctx)
    {
        var c = _http.CreateClient(HttpClientName);
        c.BaseAddress ??= new Uri("https://business.tbank.ru/openapi/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Secret(SecretToken));
        return c;
    }

    public override async Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct)
    {
        using var client = Client(ctx);
        using var resp = await client.GetAsync("api/v4/bank-accounts", ct);
        await EnsureOk(resp, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("accounts", out var arr) ? arr : doc.RootElement;

        var list = new List<ExternalAccount>();
        foreach (var a in root.EnumerateArray())
        {
            var number = Str(a, "accountNumber") ?? "";
            var status = Str(a, "status");
            if (status is not null && !status.Equals("NORM", StringComparison.OrdinalIgnoreCase) && !status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;
            var cur = Currency.FromStatement(Str(a, "currency") ?? "643");
            var name = Str(a, "name") ?? $"Р/с …{number[^Math.Min(4, number.Length)..]}";
            Money? bal = null, avail = null, blocked = null;
            if (a.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.Object)
            {
                bal = Dec(b, "balance") is { } v ? new Money(v, cur) : null;
                avail = Dec(b, "authorized") is { } av ? new Money(av, cur) : (Dec(b, "otb") is { } otb ? new Money(otb, cur) : null);
                blocked = Dec(b, "pendingPayments") is { } pp ? new Money(pp, cur) : null;
            }
            var type = (Str(a, "accountType") ?? "").ToLowerInvariant() switch
            {
                var t when t.Contains("deposit") => AccountType.Deposit,
                var t when t.Contains("saving") => AccountType.Savings,
                _ => AccountType.Checking,
            };
            list.Add(new ExternalAccount(number, name, type, cur, number, bal, avail, blocked));
        }
        return list;
    }

    public override async Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct)
    {
        using var client = Client(ctx);
        var list = new List<ExternalTransaction>();
        string? cursor = null;

        do
        {
            var url = $"api/v1/statement?accountNumber={accountExternalId}&from={range.From:yyyy-MM-dd}T00:00:00Z&to={range.To.AddDays(1):yyyy-MM-dd}T00:00:00Z&limit=5000"
                      + (cursor is not null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Request-Id", Guid.NewGuid().ToString());
            using var resp = await client.SendAsync(req, ct);
            await EnsureOk(resp, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("operations", out var ops) && !doc.RootElement.TryGetProperty("operation", out ops))
                break;

            foreach (var op in ops.EnumerateArray())
            {
                var tx = MapOperation(op, accountExternalId);
                if (tx is not null) list.Add(tx);
            }

            cursor = Str(doc.RootElement, "nextCursor");
        } while (!string.IsNullOrEmpty(cursor));

        return list;
    }

    internal static ExternalTransaction? MapOperation(JsonElement op, string accountNumber)
    {
        var status = Str(op, "status") ?? Str(op, "operationStatus");
        if (status is not null && status.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return null;

        var dateStr = Str(op, "operationDate") ?? Str(op, "date") ?? Str(op, "chargeDate") ?? Str(op, "drawDate");
        if (dateStr is null || !DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;

        var amount = Dec(op, "accountAmount") ?? Dec(op, "amount") ?? Dec(op, "operationAmount") ?? 0m;
        var type = Str(op, "typeOfOperation") ?? Str(op, "type") ?? "";
        var isDebit = type.Equals("Debit", StringComparison.OrdinalIgnoreCase) || type.Equals("Списание", StringComparison.OrdinalIgnoreCase);
        var signed = isDebit ? -Math.Abs(amount) : Math.Abs(amount);
        var cur = Currency.FromStatement(Str(op, "operationCurrencyDigitalCode") ?? Str(op, "currency") ?? "643");

        // Контрагент — противоположная сторона
        var side = isDebit ? (op.TryGetProperty("receiver", out var r) ? r : op.TryGetProperty("counterParty", out r) ? r : default)
                           : (op.TryGetProperty("payer", out var p) ? p : op.TryGetProperty("counterParty", out p) ? p : default);
        CounterpartyRaw cp = CounterpartyRaw.Empty;
        if (side.ValueKind == JsonValueKind.Object)
            cp = new CounterpartyRaw(Str(side, "name"), Str(side, "inn"), Str(side, "kpp"), Str(side, "account"), Str(side, "bic") ?? Str(side, "bankBic"), Str(side, "bankName"));

        var purpose = Str(op, "payPurpose") ?? Str(op, "paymentPurpose") ?? Str(op, "description");
        var desc = cp.Name ?? Str(op, "description") ?? Str(op, "category") ?? "Операция";
        var id = Str(op, "operationId") ?? Str(op, "id") ?? Str(op, "ucid");
        var st = status is not null && status.Contains("author", StringComparison.OrdinalIgnoreCase) ? TransactionStatus.Pending : TransactionStatus.Posted;

        return new ExternalTransaction(id, accountNumber, date, new Money(signed, cur), desc, cp, purpose, null, st, null, op.GetRawText());
    }

    private static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"T-API: {(int)resp.StatusCode} {body}");
        throw new HttpRequestException($"T-API: {(int)resp.StatusCode} {body}");
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()
        : e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;

    private static decimal? Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetDecimal()
            : v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null
            : null;
}

public static class DependencyInjection
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddTBankBusinessConnector(this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(services, TBankBusinessConnector.HttpClientName, c => c.BaseAddress = new Uri("https://business.tbank.ru/openapi/"));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IConnector, TBankBusinessConnector>(services);
        return services;
    }
}
