using CashFlow.Api;
using CashFlow.Application;
using CashFlow.Application.Seed;
using CashFlow.Connectors.Alfa.Business;
using CashFlow.Connectors.Sber.Business;
using CashFlow.Connectors.Statements;
using CashFlow.Connectors.TBank.Business;
using CashFlow.Connectors.TInvest;
using CashFlow.Infrastructure;
using CashFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CashFlow.Server;

public static class ServerHosting
{
    /// <summary>
    /// Все сервисы сервера CashFlow. <paramref name="withCookies"/> — для веб-хоста (вход через cookie Identity, Razor-страницы);
    /// встроенный сервер настольного приложения работает только по bearer-токенам.
    /// </summary>
    public static IServiceCollection AddCashFlowServer(this IServiceCollection services, IConfiguration config, bool withCookies)
    {
        var auth = services.AddAuthentication(options =>
        {
            options.DefaultScheme = withCookies ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;
            if (withCookies) options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        auth.AddIdentityCookies(); // схема Identity.Application нужна политике авторизации Identity даже при входе только по bearer
        auth.AddBearerToken(IdentityConstants.BearerScheme, o =>
        {
            o.BearerTokenExpiration = TimeSpan.FromHours(12);
            o.RefreshTokenExpiration = TimeSpan.FromDays(30);
        });
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        services.AddCashFlowInfrastructure(config);
        services.AddCashFlowApplication();
        services.AddStatementParsers();
        services.AddTInvestConnector();
        services.AddTBankBusinessConnector(config);
        services.AddSberBusinessConnector(config);
        services.AddAlfaBusinessConnector(config);
        services.AddMemoryCache();
        services.AddAuthorization();
        services.AddCashFlowBackgroundJobs(config);

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false; // self-hosted: почта не обязательна
                options.Password.RequiredLength = 10;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddErrorDescriber<RussianIdentityErrorDescriber>()
            .AddEntityFrameworkStores<CashFlowDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddApiEndpoints();

        // Письма не отправляем: self-hosted, подтверждение почты выключено. Веб-хост может заменить своей реализацией.
        services.TryAddSingleton<IEmailSender<ApplicationUser>, NoOpIdentityEmailSender>();
        return services;
    }

    /// <summary>REST для клиентов: /api/auth (login/refresh/register Identity) и /api/* поверх контрактов Application.</summary>
    public static WebApplication MapCashFlowServerApi(this WebApplication app)
    {
        app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
        app.MapCashFlowApi();
        return app;
    }

    /// <summary>Миграции и справочники при старте: пользователь просто запускает контейнер или приложение.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CashFlowDbContext>();
        await db.Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
    }
}

/// <summary>Заглушка вместо почты: ссылки подтверждения никуда не уходят (подтверждение аккаунта отключено).</summary>
public sealed class NoOpIdentityEmailSender : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
}
