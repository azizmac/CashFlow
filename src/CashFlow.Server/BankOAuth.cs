using System.Security.Claims;
using System.Security.Cryptography;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CashFlow.Server;

/// <summary>Незавершённая OAuth-авторизация: кто начал, для какого профиля, с каким state/PKCE, откуда пришёл. Живёт 15 минут в памяти.</summary>
public sealed record PendingOAuth(string UserId, Guid ProfileId, string? Name, ConnectorType Type, OAuthFlow Flow, bool FromApp);

public sealed record OAuthStartRequest(Guid ProfileId, string? Name);
public sealed record OAuthStartResponse(string Url, string State);

/// <summary>
/// Подключение банка «через авторизацию» (СберБизнес ID, T-Business ID, Alfa ID). Общее для веб-хоста и встроенного сервера приложения.
///
/// Веб: GET /oauth/{provider}/start (cookie) → страница банка → GET /oauth/{provider}/callback → /connections?connected=id.
/// Приложение: POST /api/oauth/{provider}/start (bearer) возвращает URL банка, приложение открывает его в системном браузере;
/// callback узнаёт пользователя по одноразовому state (браузер сессии не имеет) и показывает страницу «вернитесь в приложение».
/// Redirect URI = {PublicBaseUrl или хост запроса}/oauth/{provider}/callback — его регистрируют в ЛК банка
/// (для настольной сборки это http://127.0.0.1:47831/oauth/…/callback).
/// </summary>
public static class BankOAuth
{
    public const string PublicBaseUrlKey = "Integrations:PublicBaseUrl";

    public static IEndpointRouteBuilder MapBankOAuth(this IEndpointRouteBuilder app)
    {
        var schemes = new AuthorizeAttribute { AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{IdentityConstants.BearerScheme}" };

        // Старт из браузера (cookie): сразу редирект на банк
        app.MapGet("/oauth/{connector}/start", async (string connector, Guid profileId, string? name, HttpContext http,
            IEnumerable<IConnector> connectors, IMemoryCache cache, IProfileService profiles, IConfiguration config) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (uid is null) return Results.Unauthorized();
            var start = await BeginAsync(uid, connector, profileId, name, fromApp: false, http, connectors, cache, profiles, config);
            return start.Error is not null ? Results.Redirect(Fail(start.Error)) : Results.Redirect(start.Url!);
        }).RequireAuthorization(schemes);

        // Старт из приложения (bearer): вернуть URL, приложение откроет системный браузер
        app.MapPost("/api/oauth/{connector}/start", async (string connector, OAuthStartRequest r, HttpContext http,
            IEnumerable<IConnector> connectors, IMemoryCache cache, IProfileService profiles, IConfiguration config) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (uid is null) return Results.Unauthorized();
            var start = await BeginAsync(uid, connector, r.ProfileId, r.Name, fromApp: true, http, connectors, cache, profiles, config);
            return start.Error is not null
                ? Results.Json(new ApiError(start.Error), statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(new OAuthStartResponse(start.Url!, start.State!));
        }).RequireAuthorization(schemes);

        // Возврат от банка. Пользователь определяется по одноразовому state: у системного браузера приложения сессии нет.
        app.MapGet("/oauth/{connector}/callback", async (string connector, string? code, string? state, string? error, string? error_description,
            IEnumerable<IConnector> connectors, IMemoryCache cache, IConnectionsService connections, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("BankOAuth");
            PendingOAuth? pending = null;
            if (!string.IsNullOrEmpty(state) && cache.TryGetValue(CacheKey(state), out PendingOAuth? p)) pending = p;
            var fromApp = pending?.FromApp ?? false;

            if (error is not null) return Finish(fromApp, null, $"Банк отклонил авторизацию: {error} {error_description}".Trim());
            if (pending is null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Finish(fromApp, null, "Сессия авторизации не найдена или устарела — начните подключение заново");
            cache.Remove(CacheKey(state));

            var oc = Find(connectors, connector);
            if (oc is null || oc.Type != pending.Type) return Finish(fromApp, null, "Неизвестный провайдер");

            try
            {
                var secrets = await oc.ExchangeCodeAsync(code, pending.Flow, ct);
                var connName = string.IsNullOrWhiteSpace(pending.Name) ? oc.ProviderDisplayName : pending.Name!;
                var conn = await connections.CreateAsync(pending.UserId, pending.ProfileId, oc.Type, connName, secrets, ct);
                return Finish(fromApp, conn.Id, null);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "OAuth callback failed for {Connector}", connector);
                return Finish(fromApp, null, ex.Message);
            }
        }).AllowAnonymous();

        return app;
    }

    private sealed record Started(string? Url, string? State, string? Error);

    private static async Task<Started> BeginAsync(string uid, string connector, Guid profileId, string? name, bool fromApp, HttpContext http,
        IEnumerable<IConnector> connectors, IMemoryCache cache, IProfileService profiles, IConfiguration config)
    {
        var oc = Find(connectors, connector);
        if (oc is null) return new(null, null, "Неизвестный провайдер");
        if (!oc.IsConfigured) return new(null, null, $"{oc.ProviderDisplayName}: реквизиты приложения не заданы на сервере (см. Integrations в конфигурации)");
        if ((await profiles.ListAsync(uid)).All(p => p.Id != profileId)) return new(null, null, "Профиль не найден");

        var baseUrl = config[PublicBaseUrlKey] is { Length: > 0 } b ? b.TrimEnd('/') : $"{http.Request.Scheme}://{http.Request.Host}";
        var flow = new OAuthFlow(Rnd(24), $"{baseUrl}/oauth/{oc.UrlName()}/callback", Rnd(48), Rnd(16));
        cache.Set(CacheKey(flow.State), new PendingOAuth(uid, profileId, name, oc.Type, flow, fromApp), TimeSpan.FromMinutes(15));
        return new(oc.BuildAuthorizationUrl(flow), flow.State, null);
    }

    /// <summary>Веб — редирект на страницу подключений; приложение — HTML-страница, после которой можно закрыть браузер.</summary>
    private static IResult Finish(bool fromApp, Guid? connectionId, string? error)
    {
        if (!fromApp) return Results.Redirect(error is null ? $"/connections?connected={connectionId}" : Fail(error));
        var ok = error is null;
        var title = ok ? "Банк подключён" : "Не удалось подключить банк";
        var text = ok
            ? "Вернитесь в приложение CashFlow: подключение появилось в списке, первая синхронизация запустится автоматически. Эту вкладку можно закрыть."
            : System.Net.WebUtility.HtmlEncode(error!);
        var html = $$"""
            <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{title}} — CashFlow</title>
            <style>
              body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;font-family:-apple-system,system-ui,Roboto,sans-serif;color:#f3f4f6;
                   background:radial-gradient(120% 80% at 20% -10%,rgba(47,107,255,.28),transparent 60%),radial-gradient(90% 70% at 90% 15%,rgba(140,90,255,.22),transparent 60%),#08090d}
              .card{max-width:420px;margin:24px;padding:28px;border-radius:24px;background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.1);backdrop-filter:blur(30px)}
              .logo{width:52px;height:52px;border-radius:17px;background:linear-gradient(150deg,#4d7cff,#8a5cff);display:flex;align-items:center;justify-content:center;font-size:22px;margin-bottom:16px}
              h1{font-size:22px;margin:0 0 10px} p{font-size:15px;line-height:1.5;color:rgba(255,255,255,.7);margin:0}
              .err h1{color:oklch(.7 .18 22)}
            </style></head><body><div class="card {{(ok ? "" : "err")}}"><div class="logo">{{(ok ? "✓" : "!")}}</div><h1>{{title}}</h1><p>{{text}}</p></div></body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: ok ? 200 : 400);
    }

    private static string Fail(string msg) => "/connections?oauth=error&msg=" + Uri.EscapeDataString(msg);
    private static string CacheKey(string state) => "oauth:" + state;
    private static string Rnd(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Имя провайдера в URL — ConnectorType в нижнем регистре: sberbusiness, tbankbusiness, alfabusiness.</summary>
    internal static IOAuthConnector? Find(IEnumerable<IConnector> connectors, string name) =>
        connectors.OfType<IOAuthConnector>().FirstOrDefault(c => c.Type.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string UrlName(this IOAuthConnector c) => c.Type.ToString().ToLowerInvariant();
}
