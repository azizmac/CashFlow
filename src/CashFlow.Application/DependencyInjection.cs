using CashFlow.Application.Categorization;
using CashFlow.Application.Import;
using CashFlow.Application.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCashFlowApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<TransactionImportService>();
        services.AddScoped<StatementImportService>();
        services.AddScoped<ConnectionSyncService>();
        services.AddScoped<CategorizationService>();
        services.AddScoped<SeedService>();
        return services;
    }
}
