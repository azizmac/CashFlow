using CashFlow.Application;
using CashFlow.Application.Contracts;

namespace CashFlow.UI.Services;

/// <summary>
/// Кто сейчас работает с приложением. В веб-хосте — из cookie-аутентификации, в MAUI — из сохранённой сессии.
/// Страницы передают Id в прикладные сервисы; сервер всё равно проверяет владельца по своему токену.
/// </summary>
public interface ICurrentUser
{
    Task<string> IdAsync();
    Task<string?> DisplayNameAsync();
    Task<IReadOnlyList<ProfileDto>> ProfilesAsync();
}

/// <summary>Действия, которые зависят от хоста: выход, открытие внешнего OAuth-сценария банка.</summary>
public interface IAppShell
{
    /// <summary>true — приложение открыто в браузере (выход через POST-форму Identity); false — MAUI.</summary>
    bool IsBrowserHosted { get; }
    Task LogoutAsync();
    /// <summary>Запуск подключения банка через авторизацию: в вебе — переход на /oauth/{provider}/start, в MAUI — системный браузер.</summary>
    Task StartBankOAuthAsync(string providerKey, Guid profileId, string? connectionName);
}

/// <summary>Отображение времени в часовом поясе пользователя.</summary>
public static class Tz
{
    public static DateTimeOffset Local(this DateTimeOffset utc) => utc.ToLocal();
}
