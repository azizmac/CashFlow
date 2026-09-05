using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CashFlow.Application.Contracts;

namespace CashFlow.Client;

/// <summary>
/// Низкоуровневый клиент: bearer-токен из сессии, обновление токена по 401 через /api/auth/refresh, разбор ошибок ApiError.
/// JSON-настройки совпадают с сервером: camelCase, enum строками.
/// </summary>
public sealed class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http;
    private readonly ApiSession _session;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ApiClient(HttpClient http, ApiSession session)
    {
        _http = http;
        _session = session;
    }

    public ApiSession Session => _session;

    // ---------- аутентификация ----------

    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

    public async Task LoginAsync(string baseUrl, string email, string password, CancellationToken ct = default)
    {
        var url = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/auth/login?useCookies=false");
        using var resp = await SendAuthAsync(url, new { email, password }, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new ApiException(401, ExtractDetail(body) switch
            {
                "LockedOut" => "Аккаунт временно заблокирован после нескольких неудачных попыток. Попробуйте позже.",
                "NotAllowed" => "Вход для этого аккаунта запрещён.",
                "RequiresTwoFactor" => "Для этого аккаунта включена двухфакторная аутентификация, войдите через веб-версию.",
                _ => "Неверный e-mail или пароль"
            });
        }
        await EnsureOkAsync(resp, ct);
        var t = await resp.Content.ReadFromJsonAsync<TokenResponse>(Json, ct) ?? throw new ApiException(500, "Пустой ответ сервера");
        await _session.SetAsync(new SessionData(baseUrl.TrimEnd('/'), email, t.AccessToken, t.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(t.ExpiresIn)));
    }

    public async Task RegisterAsync(string baseUrl, string email, string password, CancellationToken ct = default)
    {
        var url = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/auth/register");
        using var resp = await SendAuthAsync(url, new { email, password }, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new ApiException((int)resp.StatusCode, ExtractValidationMessage(body) ?? "Не удалось зарегистрироваться");
        }
    }

    /// <summary>Запрос без токена; сетевые ошибки переводим в понятное сообщение.</summary>
    private async Task<HttpResponseMessage> SendAuthAsync(Uri url, object body, CancellationToken ct)
    {
        try { return await _http.PostAsJsonAsync(url, body, Json, ct); }
        catch (HttpRequestException ex) { throw new ApiException(0, Unreachable(url, ex)); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(0, "Сервер не ответил вовремя. Проверьте адрес и сеть."); }
    }

    private static string Unreachable(Uri url, HttpRequestException ex) => ex.InnerException is System.Net.Sockets.SocketException
        ? $"Сервер {url.GetLeftPart(UriPartial.Authority)} недоступен. Проверьте, что он запущен, и адрес указан верно."
        : $"Не удалось связаться с сервером: {ex.Message}";

    private static string? ExtractDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var s = _session.Current;
        if (s is null) return false;
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_session.Current is { } fresh && fresh.AccessToken != s.AccessToken) return true; // уже обновили параллельно
            using var resp = await _http.PostAsJsonAsync(_session.Url("api/auth/refresh"), new { refreshToken = s.RefreshToken }, Json, ct);
            if (!resp.IsSuccessStatusCode) return false;
            var t = await resp.Content.ReadFromJsonAsync<TokenResponse>(Json, ct);
            if (t is null) return false;
            await _session.SetAsync(s with { AccessToken = t.AccessToken, RefreshToken = t.RefreshToken, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(t.ExpiresIn) });
            return true;
        }
        finally { _refreshLock.Release(); }
    }

    // ---------- запросы ----------

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, _session.Url(path)), ct);
        return await ReadAsync<T>(resp, ct);
    }

    public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, _session.Url(path)), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsync<T>(resp, ct);
    }

    public async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, _session.Url(path)) { Content = body is null ? null : JsonContent.Create(body, options: Json) }, ct);
        return await ReadAsync<T>(resp, ct);
    }

    public async Task PostAsync(string path, object? body, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, _session.Url(path)) { Content = body is null ? null : JsonContent.Create(body, options: Json) }, ct);
        await EnsureOkAsync(resp, ct);
    }

    public async Task PutAsync(string path, object body, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, _session.Url(path)) { Content = JsonContent.Create(body, options: Json) }, ct);
        await EnsureOkAsync(resp, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, _session.Url(path)), ct);
        await EnsureOkAsync(resp, ct);
    }

    /// <summary>Загрузка файла (multipart). Поток читается в память заранее, чтобы запрос можно было повторить после обновления токена.</summary>
    public async Task<T> PostFileAsync<T>(string path, Stream file, string fileName, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        using var resp = await SendAsync(() =>
        {
            var content = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "files", fileName);
            return new HttpRequestMessage(HttpMethod.Post, _session.Url(path)) { Content = content };
        }, ct);
        return await ReadAsync<T>(resp, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        var resp = await SendOnceAsync(make(), ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAsync(ct))
        {
            resp.Dispose();
            resp = await SendOnceAsync(make(), ct);
        }
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _session.ClearAsync();
            throw new ApiException(401, "Сессия истекла — войдите заново");
        }
        return resp;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (_session.Current is { } s) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        try { return await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new ApiException(0, Unreachable(req.RequestUri!, ex)); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(0, "Сервер не ответил вовремя. Проверьте сеть."); }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        await EnsureOkAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new ApiException(500, "Пустой ответ сервера");
    }

    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        string? msg = null;
        try { msg = JsonSerializer.Deserialize<ApiError>(body, Json)?.Error; } catch { /* не JSON */ }
        msg ??= ExtractValidationMessage(body) ?? StatusText((int)resp.StatusCode);
        throw new ApiException((int)resp.StatusCode, msg);
    }

    private static string StatusText(int code) => code switch
    {
        400 => "Сервер отклонил запрос (400)", 403 => "Нет доступа (403)", 404 => "Не найдено (404)", 413 => "Файл слишком большой (413)",
        500 => "Внутренняя ошибка сервера (500)", 502 or 503 or 504 => $"Сервер временно недоступен ({code})", _ => $"Сервер ответил кодом {code}"
    };

    /// <summary>ValidationProblemDetails от Identity: {"errors":{"PasswordTooShort":["..."]}}.</summary>
    private static string? ExtractValidationMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                return string.Join("; ", errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())).Where(s => s is not null));
            if (doc.RootElement.TryGetProperty("title", out var title)) return title.GetString();
        }
        catch { }
        return null;
    }
}
