using CashFlow.Application;
using CashFlow.Infrastructure.Persistence;
using CashFlow.Infrastructure.Security;
using CashFlow.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Хранилище: PostgreSQL, шифрование полей, секреты. Нужно и серверу, и утилитам.</summary>
    public static IServiceCollection AddCashFlowInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EncryptionOptions>(config.GetSection(EncryptionOptions.Section));
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();

        var cs = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
        services.AddDbContext<CashFlowDbContext>(o => o.UseNpgsql(cs, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "cashflow")));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ISecretStore, DbSecretStore>();
        return services;
    }

    /// <summary>Фоновые задачи сервера (планировщик синхронизации). В утилитах и тестах не подключается.</summary>
    public static IServiceCollection AddCashFlowBackgroundJobs(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));
        services.AddHostedService<SyncScheduler>();
        return services;
    }
}
