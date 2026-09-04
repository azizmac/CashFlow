using System.Security.Claims;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using CashFlow.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CashFlow.Web.Services;

/// <summary>Текущий пользователь веб-хоста: id и имя из cookie-аутентификации, профили через прикладной сервис.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly AuthenticationStateProvider _auth;
    private readonly IProfileService _profiles;

    public CurrentUser(AuthenticationStateProvider auth, IProfileService profiles)
    {
        _auth = auth;
        _profiles = profiles;
    }

    public async Task<string> IdAsync()
    {
        var state = await _auth.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    }

    public async Task<string?> DisplayNameAsync() => (await _auth.GetAuthenticationStateAsync()).User.Identity?.Name;

    public async Task<IReadOnlyList<ProfileDto>> ProfilesAsync() => await _profiles.ListAsync(await IdAsync());
}

/// <summary>Действия хоста в браузере: выход через форму Identity, OAuth банка — обычный переход на серверный маршрут.</summary>
public sealed class WebAppShell : IAppShell
{
    private readonly NavigationManager _nav;
    public WebAppShell(NavigationManager nav) => _nav = nav;

    public bool IsBrowserHosted => true;

    public Task LogoutAsync() { _nav.NavigateTo("/Account/Logout", forceLoad: true); return Task.CompletedTask; }

    public Task StartBankOAuthAsync(string providerKey, Guid profileId, string? connectionName)
    {
        _nav.NavigateTo($"/oauth/{providerKey}/start?profileId={profileId}&name={Uri.EscapeDataString(connectionName ?? "")}", forceLoad: true);
        return Task.CompletedTask;
    }
}
