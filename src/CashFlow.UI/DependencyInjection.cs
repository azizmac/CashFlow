using Microsoft.Extensions.DependencyInjection;
using CashFlow.UI.Services;

namespace CashFlow.UI;

public static class DependencyInjection
{
    /// <summary>Сервисы общего UI (тема). Хост регистрирует ICurrentUser и IAppShell сам.</summary>
    public static IServiceCollection AddCashFlowUi(this IServiceCollection services)
    {
        services.AddScoped<ThemeService>();
        return services;
    }
}
