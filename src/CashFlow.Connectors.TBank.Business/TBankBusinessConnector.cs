using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Connectors.TBank.Business;

/// <summary>
/// Реквизиты партнёрского приложения T-ID / T-Business ID (developer.tbank.ru → Партнёрская интеграция).
/// Партнёрский доступ банк выдаёт по заявке на tid@tbank.ru (ориентир — от 30 тыс. авторизаций в месяц),
/// поэтому для одного ИП основной путь — самостоятельный токен из ЛК Т-Бизнеса. Эти настройки нужны только партнёрам.
/// Эндпоинты по документации T-ID: https://id.tbank.ru/auth/authorize, /auth/token, /auth/introspect.
/// </summary>
public sealed class TBankApiOptions
{
    public const string Section = "Integrations:TBank";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string AuthorizeUrl { get; set; } = "https://id.tbank.ru/auth/authorize";
    public string TokenUrl { get; set; } = "https://id.tbank.ru/auth/token";
    public string ApiBase { get; set; } = "https://business.tbank.ru/openapi/";
    /// <summary>Только чтение: счета, балансы, выписки (из списка scope T-ID).</summary>
    public string Scope { get; set; } = "openid accounts balance statements";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// T-API (Т-Бизнес): расчётные счета ИП/ЮЛ. Только чтение: bank-accounts + statement.
/// Два способа подключения:
///  • токен, выпущенный в ЛК Т-Бизнеса с правами только на «Счета и выписки» (секрет token) — основной;
///  • партнёрский OAuth через T-ID (секреты client_id, client_secret, refresh_token) — если у владельца сервера есть партнёрский доступ.
/// Base: https://business.tbank.ru/openapi. Выписка: GET /api/v1/statement (from включительно, to не включительно, ISO 8601 UTC,
/// порции по limit с nextCursor, withBalances=true только в первом запросе, лимит 20 запросов/с).
/// </summary>
public sealed class TBankBusinessConnector : ReadOnlyConnectorBase, IOAuthConnector
{
    public const string SecretToken = "token";
    public const string SecretClientId = "client_id";
    public const string SecretClientSecret = "client_secret";
    public const string SecretRefreshToken = "refresh_token";
    public const string HttpClientName = "tbank-business";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<TBankBusinessConnector> _log;
    private readonly TBankApiOptions _opt;

    public TBankBusinessConnector(IHttpClientFactory http, ILogger<TBankBusinessConnector> log, IOptions<TBankApiOptions> options)
    {
        _http = http;
        _log = log;
        _opt = options.Value;
    }

    public override ConnectorType Type => ConnectorType.TBankBusiness;
    public override ConnectorCapabilities Capabilities => ConnectorCapabilities.Accounts | ConnectorCapabilities.Balances | ConnectorCapabilities.Transactions;
    /// <summary>Для ручного подключения нужен только токен из ЛК; OAuth-подключение хранит client_id/client_secret/refresh_token.</summary>
    public override IReadOnlyList<string> RequiredSecrets => [SecretToken];

    // ---------- OAuth (T-ID / T-Business ID, только для партнёров) ----------

    public bool IsConfigured => _opt.IsConfigured;
    public string ProviderDisplayName => "T-Business ID (Т-Банк)";
    public string SetupHint =>
        "Только для партнёров Т-Банка: заявка на tid@tbank.ru, redirect URI «<адрес сервера>/oauth/tbankbusiness/callback», " +
        "client_id и client_secret в конфигурации сервера (Integrations:TBank). Для своего ИП проще выпустить токен в ЛК Т-Бизнеса (Интеграции → T-API).";

    public string BuildAuthorizationUrl(OAuthFlow flow)
    {
        if (!IsConfigured) throw new InvalidOperationException("T-ID не настроен (Integrations:TBank)");
        var q = new Dictionary<string, string>
        {
            ["client_id"] = _opt.ClientId!,
            ["redirect_uri"] = flow.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = _opt.Scope,
            ["state"] = flow.State,
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(flow.CodeVerifier))),
            ["code_challenge_method"] = "S256",
        };
        return _opt.AuthorizeUrl + "?" + string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<IReadOnlyDictionary<string, string>> ExchangeCodeAsync(string code, OAuthFlow flow, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("T-ID не настроен (Integrations:TBank)");
        var json = await TokenRequestAsync(_opt.ClientId!, _opt.ClientSecret!, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri,
            ["code_verifier"] = flow.CodeVerifier,
        }, ct);
        using var doc = JsonDocument.Parse(json);
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("T-ID: в ответе нет refresh_token");
        return new Dictionary<string, string>
        {
            [SecretClientId] = _opt.ClientId!,
            [SecretClientSecret] = _opt.ClientSecret!,
            [SecretRefreshToken] = refresh,
        };
    }

    private async Task<string> TokenRequestAsync(string clientId, string clientSecret, Dictionary<string, string> form, CancellationToken ct)
    {
        using var http = _http.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, _opt.TokenUrl);
        // T-ID: client_id и client_secret — в Basic-заголовке; дублируем в форме, как в документации /auth/token
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        form["client_id"] = clientId;
        form["client_secret"] = clientSecret;
        req.Content = new FormUrlEncodedContent(form);
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new UnauthorizedAccessException($"T-ID: {(int)resp.StatusCode} {json}");
        return json;
    }

    // ---------- API ----------

    /// <summary>Клиент с Bearer: либо токен из ЛК, либо access_token по refresh_token партнёрского OAuth.</summary>
    private async Task<HttpClient> ClientAsync(ConnectionContext ctx, CancellationToken ct)
    {
        string access;
        if (ctx.Secrets.TryGetValue(SecretToken, out var token) && !string.IsNullOrWhiteSpace(token))
            access = token;
        else
        {
            var json = await TokenRequestAsync(ctx.Secret(SecretClientId), ctx.Secret(SecretClientSecret), new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = ctx.Secret(SecretRefreshToken),
            }, ct);
            using var doc = JsonDocument.Parse(json);
            access = doc.RootElement.GetProperty("access_token").GetString()!;
            var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            if (refresh is not null && refresh != ctx.Secret(SecretRefreshToken) && ctx.OnSecretsRotated is not null)
                await ctx.OnSecretsRotated(new Dictionary<string, string>(ctx.Secrets) { [SecretRefreshToken] = refresh }, ct);
        }

        var c = _http.CreateClient(HttpClientName);
        c.BaseAddress ??= new Uri(_opt.ApiBase);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return c;
    }

    public override async Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct)
    {
        using var client = await ClientAsync(ctx, ct);
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
        using var client = await ClientAsync(ctx, ct);
        var list = new List<ExternalTransaction>();
        string? cursor = null;

        do
        {
            // from — включительно, to — не включительно (ISO 8601, UTC); порции по 5000 с nextCursor
            var url = $"api/v1/statement?accountNumber={accountExternalId}&from={range.From:yyyy-MM-dd}T00:00:00Z&to={range.To.AddDays(1):yyyy-MM-dd}T00:00:00Z&limit=5000&operationStatus=All"
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

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
    public static IServiceCollection AddTBankBusinessConnector(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TBankApiOptions>(config.GetSection(TBankApiOptions.Section));
        services.AddHttpClient(TBankBusinessConnector.HttpClientName, c => c.BaseAddress = new Uri("https://business.tbank.ru/openapi/"));
        services.AddSingleton<IConnector, TBankBusinessConnector>();
        return services;
    }
}
