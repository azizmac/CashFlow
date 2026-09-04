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

namespace CashFlow.Connectors.Alfa.Business;

/// <summary>
/// Реквизиты приложения в Alfa API (developers.alfabank.ru → «Alfa API», регистрация приложения, Alfa ID).
/// Все URL вынесены в конфигурацию: значения по умолчанию соответствуют публичной документации, но их нужно
/// сверить с актуальной «Инструкцией по подключению и использованию API» (partner.alfabank.ru, версия 27, апрель 2026).
/// </summary>
public sealed class AlfaApiOptions
{
    public const string Section = "Integrations:Alfa";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string AuthorizeUrl { get; set; } = "https://id.alfabank.ru/oidc/authorize";
    public string TokenUrl { get; set; } = "https://id.alfabank.ru/oidc/token";
    public string ApiBase { get; set; } = "https://baas.alfabank.ru/api/";
    /// <summary>Список счетов. Если у приложения нет права на этот метод — задайте номера счетов вручную в <see cref="Accounts"/>.</summary>
    public string AccountsPath { get; set; } = "statement/accounts";
    public string StatementPath { get; set; } = "statement/transactions";
    public string Scope { get; set; } = "openid statement";
    /// <summary>Резервный список счетов, если метод списка счетов недоступен.</summary>
    public List<string> Accounts { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Alfa API (Альфа-Бизнес Онлайн) — только чтение: счета и выписка. OAuth 2.0 Authorization Code через Alfa ID,
/// без mTLS. Секреты подключения: client_id, client_secret, refresh_token.
/// Формат выписки у Альфы структурно повторяет Сбер (transactions[] с rurTransfer), поэтому маппинг общий.
/// </summary>
public sealed class AlfaBusinessConnector : ReadOnlyConnectorBase, IOAuthConnector
{
    public const string SecretClientId = "client_id";
    public const string SecretClientSecret = "client_secret";
    public const string SecretRefreshToken = "refresh_token";
    public const string HttpClientName = "alfa-business";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<AlfaBusinessConnector> _log;
    private readonly AlfaApiOptions _opt;

    public AlfaBusinessConnector(IHttpClientFactory http, ILogger<AlfaBusinessConnector> log, IOptions<AlfaApiOptions> options)
    {
        _http = http;
        _log = log;
        _opt = options.Value;
    }

    public override ConnectorType Type => ConnectorType.AlfaBusiness;
    public override ConnectorCapabilities Capabilities => ConnectorCapabilities.Accounts | ConnectorCapabilities.Transactions;
    public override IReadOnlyList<string> RequiredSecrets => [SecretClientId, SecretClientSecret, SecretRefreshToken];

    // ---------- OAuth (Alfa ID) ----------

    public bool IsConfigured => _opt.IsConfigured;
    public string ProviderDisplayName => "Alfa ID (Альфа-Бизнес)";
    public string SetupHint =>
        "На developers.alfabank.ru зарегистрируйте приложение Alfa API с redirect URI «<адрес сервера>/oauth/alfabusiness/callback» " +
        "и правами только на счета и выписки; client_id и client_secret пропишите в конфигурации сервера (Integrations:Alfa). " +
        "URL авторизации и методов сверьте с актуальной инструкцией банка — они тоже задаются в конфигурации.";

    public string BuildAuthorizationUrl(OAuthFlow flow)
    {
        if (!IsConfigured) throw new InvalidOperationException("Alfa API не настроен (Integrations:Alfa)");
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _opt.ClientId!,
            ["redirect_uri"] = flow.RedirectUri,
            ["scope"] = _opt.Scope,
            ["state"] = flow.State,
            ["nonce"] = flow.Nonce,
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(flow.CodeVerifier))),
            ["code_challenge_method"] = "S256",
        };
        return _opt.AuthorizeUrl + "?" + string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<IReadOnlyDictionary<string, string>> ExchangeCodeAsync(string code, OAuthFlow flow, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Alfa API не настроен (Integrations:Alfa)");
        var json = await TokenRequestAsync(_opt.ClientId!, _opt.ClientSecret!, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri,
            ["code_verifier"] = flow.CodeVerifier,
        }, ct);
        using var doc = JsonDocument.Parse(json);
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Alfa ID: в ответе нет refresh_token");
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
        // Alfa ID принимает client_id/client_secret в Basic-заголовке; дублируем в форме для совместимости
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        form["client_id"] = clientId;
        form["client_secret"] = clientSecret;
        req.Content = new FormUrlEncodedContent(form);
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new UnauthorizedAccessException($"Alfa ID: {(int)resp.StatusCode} {json}");
        return json;
    }

    // ---------- API ----------

    private async Task<HttpClient> OpenAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var json = await TokenRequestAsync(ctx.Secret(SecretClientId), ctx.Secret(SecretClientSecret), new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = ctx.Secret(SecretRefreshToken),
        }, ct);
        using var doc = JsonDocument.Parse(json);
        var access = doc.RootElement.GetProperty("access_token").GetString()!;
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (refresh is not null && refresh != ctx.Secret(SecretRefreshToken) && ctx.OnSecretsRotated is not null)
            await ctx.OnSecretsRotated(new Dictionary<string, string>(ctx.Secrets) { [SecretRefreshToken] = refresh }, ct);

        var http = _http.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(_opt.ApiBase);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public override async Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct)
    {
        using var http = await OpenAsync(ctx, ct);
        var list = new List<ExternalAccount>();
        try
        {
            using var resp = await http.GetAsync(_opt.AccountsPath, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
                    : doc.RootElement.TryGetProperty("accounts", out var arr) ? arr
                    : doc.RootElement.TryGetProperty("_embedded", out var emb) && emb.TryGetProperty("accounts", out arr) ? arr : default;
                if (root.ValueKind == JsonValueKind.Array)
                    foreach (var a in root.EnumerateArray())
                    {
                        var number = Str(a, "accountNumber") ?? Str(a, "number") ?? "";
                        if (number.Length == 0) continue;
                        var status = Str(a, "status") ?? Str(a, "state");
                        if (status is not null && status.Contains("clos", StringComparison.OrdinalIgnoreCase)) continue;
                        var cur = Currency.FromStatement(Str(a, "currency") ?? Str(a, "currencyCode") ?? "643");
                        Money? bal = Dec(a, "amount") is { } v ? new Money(v, cur) : null;
                        list.Add(new ExternalAccount(number, Str(a, "name") ?? $"Р/с …{number[^4..]}", AccountType.Checking, cur, number, bal));
                    }
            }
            else if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException($"Alfa API: {(int)resp.StatusCode} {json}");
            else _log.LogWarning("Alfa API accounts: {Status} {Body}", (int)resp.StatusCode, json);
        }
        catch (HttpRequestException ex) { _log.LogWarning(ex, "Alfa API: список счетов недоступен"); }

        // Резерв: счета из конфигурации
        foreach (var n in _opt.Accounts.Where(n => list.All(a => a.ExternalId != n)))
            list.Add(new ExternalAccount(n, $"Альфа р/с …{n[^Math.Min(4, n.Length)..]}", AccountType.Checking, Currency.RUB, n, null));
        return list;
    }

    public override async Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct)
    {
        using var http = await OpenAsync(ctx, ct);
        var list = new List<ExternalTransaction>();
        for (var day = range.From; day <= range.To; day = day.AddDays(1))
        {
            for (var page = 1; ; page++)
            {
                using var resp = await http.GetAsync($"{_opt.StatementPath}?accountNumber={accountExternalId}&statementDate={day:yyyy-MM-dd}&page={page}&curFormat=curTransfer", ct);
                if (resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent) break;
                var json = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                        throw new UnauthorizedAccessException($"Alfa API: {(int)resp.StatusCode} {json}");
                    if (json.Contains("NOT_READY", StringComparison.OrdinalIgnoreCase) || json.Contains("не сформирована", StringComparison.OrdinalIgnoreCase)) break;
                    throw new HttpRequestException($"Alfa API: {(int)resp.StatusCode} {json}");
                }
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("transactions", out var txs) || txs.ValueKind != JsonValueKind.Array || txs.GetArrayLength() == 0) break;
                foreach (var t in txs.EnumerateArray())
                {
                    var tx = MapTransaction(t, accountExternalId);
                    if (tx is not null) list.Add(tx);
                }
                var hasNext = doc.RootElement.TryGetProperty("_links", out var links) && links.TryGetProperty("next", out _);
                if (!hasNext) break;
            }
        }
        return list;
    }

    internal static ExternalTransaction? MapTransaction(JsonElement t, string accountNumber)
    {
        var dateStr = Str(t, "transactionDate") ?? Str(t, "operationDate") ?? Str(t, "documentDate");
        if (dateStr is null || !DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;

        decimal amount = 0m;
        var cur = Currency.RUB;
        if (t.TryGetProperty("amount", out var am))
        {
            if (am.ValueKind == JsonValueKind.Object)
            {
                amount = Dec(am, "amount") ?? 0m;
                cur = Currency.FromStatement(Str(am, "currencyName") ?? Str(am, "currency") ?? "RUB");
            }
            else amount = Dec(t, "amount") ?? 0m;
        }
        var isDebit = (Str(t, "direction") ?? "").Equals("DEBIT", StringComparison.OrdinalIgnoreCase);
        var signed = isDebit ? -Math.Abs(amount) : Math.Abs(amount);

        CounterpartyRaw cp = CounterpartyRaw.Empty;
        if (t.TryGetProperty("rurTransfer", out var rt) && rt.ValueKind == JsonValueKind.Object)
        {
            cp = isDebit
                ? new CounterpartyRaw(Str(rt, "payeeName"), Str(rt, "payeeInn"), Str(rt, "payeeKpp"), Str(rt, "payeeAccount"), Str(rt, "payeeBankBic"), Str(rt, "payeeBankName"))
                : new CounterpartyRaw(Str(rt, "payerName"), Str(rt, "payerInn"), Str(rt, "payerKpp"), Str(rt, "payerAccount"), Str(rt, "payerBankBic"), Str(rt, "payerBankName"));
        }
        var purpose = Str(t, "paymentPurpose");
        var id = Str(t, "uuid") ?? Str(t, "transactionId") ?? Str(t, "number");
        return new ExternalTransaction(id, accountNumber, date, new Money(signed, cur), cp.Name ?? purpose ?? "Операция", cp, purpose, null, TransactionStatus.Posted, null, t.GetRawText());
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
    public static IServiceCollection AddAlfaBusinessConnector(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AlfaApiOptions>(config.GetSection(AlfaApiOptions.Section));
        services.AddHttpClient(AlfaBusinessConnector.HttpClientName);
        services.AddSingleton<IConnector, AlfaBusinessConnector>();
        return services;
    }
}
