using System.Security.Claims;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using CashFlow.Client;
using CashFlow.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace CashFlow.Maui.Services;

/// <summary>Сессия в защищённом хранилище платформы (Keychain, Keystore, DPAPI через SecureStorage).</summary>
public sealed class SecureSessionStore : ISessionStore
{
    private const string Key = "cashflow.session";

    public async Task<SessionData?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(Key);
            return json is null ? null : System.Text.Json.JsonSerializer.Deserialize<SessionData>(json, ApiClient.Json);
        }
        catch { return null; }
    }

    public Task SaveAsync(SessionData data) => SecureStorage.Default.SetAsync(Key, System.Text.Json.JsonSerializer.Serialize(data, ApiClient.Json));

    public Task ClearAsync() { SecureStorage.Default.Remove(Key); return Task.CompletedTask; }
}

/// <summary>Состояние аутентификации для AuthorizeView и [Authorize]: есть сессия — вошли.</summary>
public sealed class MauiAuthStateProvider : AuthenticationStateProvider
{
    private readonly ApiSession _session;

    public MauiAuthStateProvider(ApiSession session)
    {
        _session = session;
        _session.Changed += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var s = _session.Current;
        var identity = s is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, s.Email), new Claim(ClaimTypes.NameIdentifier, s.Email)], "bearer");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}

/// <summary>Кто вошёл: e-mail из сессии. Настоящий userId знает только сервер и подставляет его из токена.</summary>
public sealed class MauiCurrentUser : ICurrentUser
{
    private readonly ApiSession _session;
    private readonly IProfileService _profiles;

    public MauiCurrentUser(ApiSession session, IProfileService profiles)
    {
        _session = session;
        _profiles = profiles;
    }

    public Task<string> IdAsync() => Task.FromResult(_session.Current?.Email ?? throw new UnauthorizedAccessException());
    public Task<string?> DisplayNameAsync() => Task.FromResult(_session.Current?.Email);
    public async Task<IReadOnlyList<ProfileDto>> ProfilesAsync() => await _profiles.ListAsync(await IdAsync());
}

/// <summary>
/// Выход и OAuth банка в MAUI. Сервер по bearer-токену готовит авторизацию (state, PKCE) и отдаёт URL банка,
/// приложение открывает его в системном браузере; после возврата банка на сервер подключение появится в списке.
/// </summary>
public sealed class MauiAppShell : IAppShell
{
    private sealed record OAuthStartResponse(string Url, string State);

    private readonly ApiSession _session;
    private readonly ApiClient _api;
    public MauiAppShell(ApiSession session, ApiClient api) { _session = session; _api = api; }

    public bool IsBrowserHosted => false;

    public Task LogoutAsync() => _session.ClearAsync();

    public async Task StartBankOAuthAsync(string providerKey, Guid profileId, string? connectionName)
    {
        var start = await _api.PostAsync<OAuthStartResponse>($"api/oauth/{providerKey}/start", new { profileId, name = connectionName });
        await Launcher.Default.OpenAsync(new Uri(start.Url));
    }
}
