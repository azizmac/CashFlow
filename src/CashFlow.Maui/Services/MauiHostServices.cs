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

/// <summary>Выход и OAuth банка в MAUI: OAuth открывается в системном браузере на сервере (там нужна cookie-сессия сервера).</summary>
public sealed class MauiAppShell : IAppShell
{
    private readonly ApiSession _session;
    public MauiAppShell(ApiSession session) => _session = session;

    public bool IsBrowserHosted => false;

    public Task LogoutAsync() => _session.ClearAsync();

    public async Task StartBankOAuthAsync(string providerKey, Guid profileId, string? connectionName)
    {
        var url = _session.Url($"oauth/{providerKey}/start?profileId={profileId}&name={Uri.EscapeDataString(connectionName ?? "")}");
        await Launcher.Default.OpenAsync(url);
    }
}
