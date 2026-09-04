using System.Security.Claims;
using System.Security.Cryptography;
using CashFlow.Application;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace CashFlow.Web.Services;

/// <summary>Незавершённая OAuth-авторизация: кто начал, для какого профиля, с каким state/PKCE. Живёт 15 минут в памяти.</summary>
public sealed record PendingOAuth(string UserId, Guid ProfileId, string? Name, ConnectorType Type, OAuthFlow Flow);

/// <summary>
/// Подключение банка «через авторизацию»: /oauth/{provider}/start отправляет пользователя на страницу входа банка
/// (СберБизнес ID, T-Business ID, Alfa ID), /oauth/{provider}/callback обменивает code на токены и создаёт Connection.
/// Реквизиты приложения берутся из конфигурации сервера (Integrations:*), пользователь ничего не вводит.
/// </summary>
public static class BankOAuthEndpoints
{
    public const string PublicBaseUrlKey = "Integrations:PublicBaseUrl";

    public static IEndpointRouteBuilder MapBankOAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/oauth").RequireAuthorization();

        group.MapGet("/{connector}/start", async (string connector, Guid profileId, string? name, HttpContext http,
            IEnumerable<IConnector> connectors, IMemoryCache cache, IProfileService profiles, IConfiguration config) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (uid is null) return Results.Unauthorized();
            var oc = Find(connectors, connector);
            if (oc is null || !oc.IsConfigured) return Results.Redirect(Fail("Провайдер не настроен на сервере (см. Integrations в конфигурации)"));
            if ((await profiles.ListAsync(uid)).All(p => p.Id != profileId)) return Results.Redirect(Fail("Профиль не найден"));

            var baseUrl = config[PublicBaseUrlKey] is { Length: > 0 } b ? b.TrimEnd('/') : $"{http.Request.Scheme}://{http.Request.Host}";
            var flow = new OAuthFlow(Rnd(24), $"{baseUrl}/oauth/{oc.UrlName()}/callback", Rnd(48), Rnd(16));
            cache.Set(CacheKey(flow.State), new PendingOAuth(uid, profileId, name, oc.Type, flow), TimeSpan.FromMinutes(15));
            return Results.Redirect(oc.BuildAuthorizationUrl(flow));
        });

        group.MapGet("/{connector}/callback", async (string connector, string? code, string? state, string? error, string? error_description,
            HttpContext http, IEnumerable<IConnector> connectors, IMemoryCache cache, IConnectionsService connections, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("BankOAuth");
            if (error is not null) return Results.Redirect(Fail($"Банк отклонил авторизацию: {error} {error_description}".Trim()));

            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (uid is null || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(code)
                || !cache.TryGetValue(CacheKey(state), out PendingOAuth? pending) || pending is null || pending.UserId != uid)
                return Results.Redirect(Fail("Сессия авторизации не найдена или устарела — начните подключение заново"));
            cache.Remove(CacheKey(state));

            var oc = Find(connectors, connector);
            if (oc is null || oc.Type != pending.Type) return Results.Redirect(Fail("Неизвестный провайдер"));

            try
            {
                var secrets = await oc.ExchangeCodeAsync(code, pending.Flow, ct);
                var connName = string.IsNullOrWhiteSpace(pending.Name) ? oc.ProviderDisplayName : pending.Name!;
                var conn = await connections.CreateAsync(uid, pending.ProfileId, oc.Type, connName, secrets, ct);
                return Results.Redirect($"/connections?connected={conn.Id}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "OAuth callback failed for {Connector}", connector);
                return Results.Redirect(Fail(ex.Message));
            }
        });

        return app;
    }

    private static string Fail(string msg) => "/connections?oauth=error&msg=" + Uri.EscapeDataString(msg);
    private static string CacheKey(string state) => "oauth:" + state;
    private static string Rnd(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Имя провайдера в URL — ConnectorType в нижнем регистре: sberbusiness, tbankbusiness, alfabusiness.</summary>
    internal static IOAuthConnector? Find(IEnumerable<IConnector> connectors, string name) =>
        connectors.OfType<IOAuthConnector>().FirstOrDefault(c => c.Type.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string UrlName(this IOAuthConnector c) => c.Type.ToString().ToLowerInvariant();
}
