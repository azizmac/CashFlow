using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Connectors.Sber.Business;

/// <summary>
/// Реквизиты приложения в Sber API (прямая интеграция ИП/ЮЛ). Задаются владельцем сервера в конфигурации
/// (Integrations:Sber:*), пользователи их не вводят — они только авторизуются в СберБизнес ID.
/// </summary>
public sealed class SberApiOptions
{
    public const string Section = "Integrations:Sber";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Клиентский сертификат для mTLS: путь к .pfx или base64 содержимого.</summary>
    public string? CertPfxPath { get; set; }
    public string? CertPfxBase64 { get; set; }
    public string? CertPassword { get; set; }
    /// <summary>Страница входа СберБизнес ID (Authorization Code + PKCE).</summary>
    public string AuthorizeUrl { get; set; } = "https://sbi.sberbank.ru:9443/ic/sso/api/v2/oauth/authorize";
    public string TokenUrl { get; set; } = "https://fintech.sberbank.ru:9443/ic/sso/api/v2/oauth/token";
    public string ApiBase { get; set; } = "https://fintech.sberbank.ru:9443/fintech/api/";
    /// <summary>Только чтение: список счетов и выписки. Платёжные scope не запрашиваются.</summary>
    public string Scope { get; set; } = "openid GET_CLIENT_ACCOUNTS GET_STATEMENT_ACCOUNT";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
                                && (!string.IsNullOrWhiteSpace(CertPfxPath) || !string.IsNullOrWhiteSpace(CertPfxBase64));
}

/// <summary>
/// Sber API (СберБизнес) — только чтение: client-info, statement/summary, statement/transactions.
/// Транспорт: mTLS (клиентский сертификат из ЛК Sber API) + OAuth2.
/// Два способа подключения:
///  • вручную — пользователь вводит client_id/client_secret/refresh_token/сертификат;
///  • через авторизацию — сервер настроен (Integrations:Sber), пользователь входит в СберБизнес ID, приложение получает токены само.
/// Секреты подключения: client_id, client_secret, refresh_token, cert_pfx_base64, cert_password.
/// </summary>
public sealed class SberBusinessConnector : ReadOnlyConnectorBase, IOAuthConnector
{
    public const string SecretClientId = "client_id";
    public const string SecretClientSecret = "client_secret";
    public const string SecretRefreshToken = "refresh_token";
    public const string SecretCertPfx = "cert_pfx_base64";
    public const string SecretCertPassword = "cert_password";

    private readonly ILogger<SberBusinessConnector> _log;
    private readonly SberApiOptions _opt;

    public SberBusinessConnector(ILogger<SberBusinessConnector> log, IOptions<SberApiOptions> options)
    {
        _log = log;
        _opt = options.Value;
    }

    public override ConnectorType Type => ConnectorType.SberBusiness;
    public override ConnectorCapabilities Capabilities => ConnectorCapabilities.Accounts | ConnectorCapabilities.Balances | ConnectorCapabilities.Transactions;
    public override IReadOnlyList<string> RequiredSecrets => [SecretClientId, SecretClientSecret, SecretRefreshToken, SecretCertPfx, SecretCertPassword];

    // ---------- OAuth (СберБизнес ID) ----------

    public bool IsConfigured => _opt.IsConfigured;
    public string ProviderDisplayName => "СберБизнес ID";
    public string SetupHint =>
        "В СберБизнес: Настройки → Интеграции → Sber API (прямая интеграция): зарегистрируйте приложение, укажите redirect URI " +
        "«<адрес сервера>/oauth/sberbusiness/callback», права только «Информация о счетах» и «Выписки», выпустите сертификат. " +
        "client_id, client_secret и сертификат пропишите в конфигурации сервера (Integrations:Sber).";

    public string BuildAuthorizationUrl(OAuthFlow flow)
    {
        if (!IsConfigured) throw new InvalidOperationException("Sber API не настроен (Integrations:Sber)");
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _opt.ClientId!,
            ["scope"] = _opt.Scope,
            ["state"] = flow.State,
            ["nonce"] = flow.Nonce,
            ["redirect_uri"] = flow.RedirectUri,
            ["code_challenge"] = Pkce.Challenge(flow.CodeVerifier),
            ["code_challenge_method"] = "S256",
        };
        return _opt.AuthorizeUrl + "?" + string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<IReadOnlyDictionary<string, string>> ExchangeCodeAsync(string code, OAuthFlow flow, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Sber API не настроен (Integrations:Sber)");
        var pfxBase64 = LoadCertBase64();
        using var http = CreateMtlsClient(pfxBase64, _opt.CertPassword ?? "");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri,
            ["client_id"] = _opt.ClientId!,
            ["client_secret"] = _opt.ClientSecret!,
            ["code_verifier"] = flow.CodeVerifier,
        });
        using var resp = await http.PostAsync(_opt.TokenUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new UnauthorizedAccessException($"Sber OAuth (code exchange): {(int)resp.StatusCode} {json}");
        using var doc = JsonDocument.Parse(json);
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Sber OAuth: в ответе нет refresh_token");

        return new Dictionary<string, string>
        {
            [SecretClientId] = _opt.ClientId!,
            [SecretClientSecret] = _opt.ClientSecret!,
            [SecretRefreshToken] = refresh,
            [SecretCertPfx] = pfxBase64,
            [SecretCertPassword] = _opt.CertPassword ?? "",
        };
    }

    private string LoadCertBase64()
    {
        if (!string.IsNullOrWhiteSpace(_opt.CertPfxBase64)) return _opt.CertPfxBase64.Trim();
        if (!string.IsNullOrWhiteSpace(_opt.CertPfxPath)) return Convert.ToBase64String(File.ReadAllBytes(_opt.CertPfxPath));
        throw new InvalidOperationException("Не задан сертификат Sber API (Integrations:Sber:CertPfxPath или CertPfxBase64)");
    }

    private HttpClient CreateMtlsClient(string pfxBase64, string password)
    {
        var handler = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
        var pfx = Convert.FromBase64String(pfxBase64);
        handler.ClientCertificates.Add(X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet));
        return new HttpClient(handler) { BaseAddress = new Uri(_opt.ApiBase) };
    }

    // ---------- Сессия API ----------

    private sealed class Session : IDisposable
    {
        public required HttpClient Http { get; init; }
        public void Dispose() => Http.Dispose();
    }

    private async Task<Session> OpenAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var http = CreateMtlsClient(ctx.Secret(SecretCertPfx), ctx.Secret(SecretCertPassword));

        // OAuth2: обновляем access_token по refresh_token. Refresh-токен одноразовый — новый сохраняем через OnSecretsRotated.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = ctx.Secret(SecretRefreshToken),
            ["client_id"] = ctx.Secret(SecretClientId),
            ["client_secret"] = ctx.Secret(SecretClientSecret),
        });
        using var resp = await http.PostAsync(_opt.TokenUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            http.Dispose();
            throw new UnauthorizedAccessException($"Sber OAuth: {(int)resp.StatusCode} {json}");
        }
        using var doc = JsonDocument.Parse(json);
        var access = doc.RootElement.GetProperty("access_token").GetString()!;
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (refresh is not null && refresh != ctx.Secret(SecretRefreshToken) && ctx.OnSecretsRotated is not null)
        {
            var updated = new Dictionary<string, string>(ctx.Secrets) { [SecretRefreshToken] = refresh };
            await ctx.OnSecretsRotated(updated, ct);
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new Session { Http = http };
    }

    public override async Task<IReadOnlyList<ExternalAccount>> GetAccountsAsync(ConnectionContext ctx, CancellationToken ct)
    {
        using var s = await OpenAsync(ctx, ct);
        using var resp = await s.Http.GetAsync("v1/client-info", ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw Http(resp, json);
        using var doc = JsonDocument.Parse(json);

        var list = new List<ExternalAccount>();
        if (!doc.RootElement.TryGetProperty("accounts", out var accounts)) return list;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        foreach (var a in accounts.EnumerateArray())
        {
            var number = Str(a, "number") ?? "";
            var state = Str(a, "state");
            if (state is not null && !state.Equals("OPEN", StringComparison.OrdinalIgnoreCase)) continue;
            var cur = Currency.FromStatement(Str(a, "currencyCode") ?? "643");
            var name = Str(a, "name") ?? $"Р/с …{number[^Math.Min(4, number.Length)..]}";

            Money? balance = null;
            try
            {
                using var sr = await s.Http.GetAsync($"v2/statement/summary?accountNumber={number}&statementDate={today.AddDays(-1):yyyy-MM-dd}", ct);
                if (sr.IsSuccessStatusCode)
                {
                    using var sd = JsonDocument.Parse(await sr.Content.ReadAsStringAsync(ct));
                    if (sd.RootElement.TryGetProperty("closingBalance", out var cb) && cb.TryGetProperty("amount", out var amt))
                        balance = new Money(amt.GetDecimal(), cur);
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "Sber summary unavailable for {Acc}", number); }

            list.Add(new ExternalAccount(number, name, AccountType.Checking, cur, number, balance));
        }
        return list;
    }

    public override async Task<IReadOnlyList<ExternalTransaction>> GetTransactionsAsync(ConnectionContext ctx, string accountExternalId, DateRange range, CancellationToken ct)
    {
        using var s = await OpenAsync(ctx, ct);
        var list = new List<ExternalTransaction>();

        // Выписка запрашивается по дням, постранично.
        for (var day = range.From; day <= range.To; day = day.AddDays(1))
        {
            for (var page = 1; ; page++)
            {
                using var resp = await s.Http.GetAsync($"v2/statement/transactions?accountNumber={accountExternalId}&statementDate={day:yyyy-MM-dd}&page={page}", ct);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || resp.StatusCode == System.Net.HttpStatusCode.NoContent) break;
                var json = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    // Выписка за день ещё не сформирована — типичный ответ для текущего дня
                    if (json.Contains("STATEMENT_NOT_READY", StringComparison.OrdinalIgnoreCase) || json.Contains("WORKFLOW_FAULT", StringComparison.OrdinalIgnoreCase)) break;
                    throw Http(resp, json);
                }
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("transactions", out var txs) || txs.GetArrayLength() == 0) break;

                foreach (var t in txs.EnumerateArray())
                {
                    var tx = MapTransaction(t, accountExternalId);
                    if (tx is not null) list.Add(tx);
                }
                if (txs.GetArrayLength() < 100) break;
            }
        }
        return list;
    }

    internal static ExternalTransaction? MapTransaction(JsonElement t, string accountNumber)
    {
        var dateStr = Str(t, "operationDate") ?? Str(t, "documentDate");
        if (dateStr is null || !DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;

        var amount = Dec(t, "amount") ?? 0m;
        var isDebit = (Str(t, "direction") ?? "").Equals("DEBIT", StringComparison.OrdinalIgnoreCase);
        var signed = isDebit ? -Math.Abs(amount) : Math.Abs(amount);
        var cur = Currency.FromStatement(Str(t, "currencyCode") ?? "643");

        CounterpartyRaw cp = CounterpartyRaw.Empty;
        if (t.TryGetProperty("rurTransfer", out var rt) && rt.ValueKind == JsonValueKind.Object)
        {
            cp = isDebit
                ? new CounterpartyRaw(Str(rt, "payeeName"), Str(rt, "payeeInn"), Str(rt, "payeeKpp"), Str(rt, "payeeAccount"), Str(rt, "payeeBankBic"), Str(rt, "payeeBankName"))
                : new CounterpartyRaw(Str(rt, "payerName"), Str(rt, "payerInn"), Str(rt, "payerKpp"), Str(rt, "payerAccount"), Str(rt, "payerBankBic"), Str(rt, "payerBankName"));
        }

        var purpose = Str(t, "paymentPurpose");
        var desc = cp.Name ?? purpose ?? "Операция";
        var id = Str(t, "uuid") ?? Str(t, "number");
        return new ExternalTransaction(id, accountNumber, date, new Money(signed, cur), desc, cp, purpose, null, TransactionStatus.Posted, null, t.GetRawText());
    }

    private static Exception Http(HttpResponseMessage resp, string body) =>
        resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? new UnauthorizedAccessException($"Sber API: {(int)resp.StatusCode} {body}")
            : new HttpRequestException($"Sber API: {(int)resp.StatusCode} {body}");

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetDecimal()
            : v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null
            : null;
}

/// <summary>PKCE (RFC 7636): code_verifier → code_challenge S256.</summary>
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(24));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class DependencyInjection
{
    public static IServiceCollection AddSberBusinessConnector(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SberApiOptions>(config.GetSection(SberApiOptions.Section));
        services.AddSingleton<IConnector, SberBusinessConnector>();
        return services;
    }
}
