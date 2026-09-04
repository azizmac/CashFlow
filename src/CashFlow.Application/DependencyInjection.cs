using CashFlow.Application.Categorization;
using CashFlow.Application.Connections;
using CashFlow.Application.Identity;
using CashFlow.Application.Import;
using CashFlow.Application.Ledger;
using CashFlow.Application.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCashFlowApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Внутренние сервисы (импорт, синхронизация, категоризация)
        services.AddScoped<TransactionImportService>();
        services.AddScoped<StatementImportService>();
        services.AddScoped<ConnectionSyncService>();
        services.AddScoped<CategorizationService>();
        services.AddScoped<SeedService>();

        // Контракты для UI и API — серверные реализации поверх EF Core
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ILedgerQueries, LedgerQueries>();
        services.AddScoped<ILedgerCommands, LedgerCommands>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IConnectionsService, ConnectionsService>();
        return services;
    }
}
