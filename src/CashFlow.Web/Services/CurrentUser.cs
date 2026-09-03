using System.Security.Claims;
using CashFlow.Application;
using CashFlow.Domain.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace CashFlow.Web.Services;

/// <summary>Текущий пользователь + гарантия, что у него есть хотя бы один профиль.</summary>
public sealed class CurrentUser
{
    private readonly AuthenticationStateProvider _auth;
    private readonly IUnitOfWork _uow;

    public CurrentUser(AuthenticationStateProvider auth, IUnitOfWork uow)
    {
        _auth = auth;
        _uow = uow;
    }

    public async Task<string> IdAsync()
    {
        var state = await _auth.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    }

    public async Task<List<FinancialProfile>> ProfilesAsync()
    {
        var uid = await IdAsync();
        var list = _uow.Profiles.Query().Where(p => p.UserId == uid).OrderBy(p => p.CreatedAt).ToList();
        if (list.Count == 0)
        {
            var p = new FinancialProfile(uid, ProfileKind.Individual, "Личное");
            await _uow.Profiles.AddAsync(p);
            await _uow.SaveChangesAsync();
            list.Add(p);
        }
        return list;
    }
}
