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
using CashFlow.Web.Components;
using CashFlow.Web.Components.Account;
using CashFlow.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Два способа входа: cookie для браузера и bearer-токен Identity для MAUI-клиента (/api/auth/login?useCookies=false)
var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
auth.AddIdentityCookies();
auth.AddBearerToken(IdentityConstants.BearerScheme, o =>
{
    o.BearerTokenExpiration = TimeSpan.FromHours(12);
    o.RefreshTokenExpiration = TimeSpan.FromDays(30);
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddCashFlowInfrastructure(builder.Configuration);
builder.Services.AddCashFlowApplication();
builder.Services.AddStatementParsers();
builder.Services.AddTInvestConnector();
builder.Services.AddTBankBusinessConnector(builder.Configuration);
builder.Services.AddSberBusinessConnector(builder.Configuration);
builder.Services.AddAlfaBusinessConnector(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();
builder.Services.AddCashFlowBackgroundJobs(builder.Configuration);
builder.Services.AddScoped<CashFlow.UI.Services.ICurrentUser, CurrentUser>();
builder.Services.AddScoped<CashFlow.UI.Services.IAppShell, WebAppShell>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // self-hosted: почта не обязательна
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<CashFlowDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Миграции + справочники при старте (self-hosted: пользователь просто запускает контейнер)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CashFlowDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapBankOAuth(); // /oauth/{provider}/start|callback — подключение банка через авторизацию
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>(); // login/refresh для MAUI-клиента (bearer-токены)
app.MapCashFlowApi(); // REST поверх контрактов Application — единственный вход для мобильного клиента

app.Run();
