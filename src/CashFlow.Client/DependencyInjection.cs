using CashFlow.Application;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Client;

public static class DependencyInjection
{
    /// <summary>Контракты Application поверх REST сервера CashFlow. Хранилище сессии регистрирует хост (MAUI: SecureStorage).</summary>
    public static IServiceCollection AddCashFlowApiClient(this IServiceCollection services)
    {
        services.AddSingleton<ApiSession>();
        services.AddSingleton<ApiClient>(sp => new ApiClient(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, sp.GetRequiredService<ApiSession>()));
        services.AddSingleton<IProfileService, HttpProfileService>();
        services.AddSingleton<ILedgerQueries, HttpLedgerQueries>();
        services.AddSingleton<ILedgerCommands, HttpLedgerCommands>();
        services.AddSingleton<ICategoryService, HttpCategoryService>();
        services.AddSingleton<IImportService, HttpImportService>();
        services.AddSingleton<IConnectionsService, HttpConnectionsService>();
        return services;
    }
}
