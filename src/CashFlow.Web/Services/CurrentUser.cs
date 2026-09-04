using System.Security.Claims;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace CashFlow.Web.Services;

/// <summary>Текущий пользователь Blazor-хоста: id из cookie-аутентификации, профили через прикладной сервис.</summary>
public sealed class CurrentUser
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

    public async Task<IReadOnlyList<ProfileDto>> ProfilesAsync() => await _profiles.ListAsync(await IdAsync());
}

/// <summary>Отображение времени в часовом поясе пользователя (CASHFLOW_TZ).</summary>
public static class Tz
{
    public static DateTimeOffset Local(this DateTimeOffset utc) => utc.ToLocal();
}
