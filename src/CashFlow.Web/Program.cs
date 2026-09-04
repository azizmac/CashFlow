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

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddCashFlowInfrastructure(builder.Configuration);
builder.Services.AddCashFlowApplication();
builder.Services.AddStatementParsers();
builder.Services.AddTInvestConnector();
builder.Services.AddTBankBusinessConnector(builder.Configuration);
builder.Services.AddSberBusinessConnector(builder.Configuration);
builder.Services.AddAlfaBusinessConnector(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<LedgerQueries>();
builder.Services.AddScoped<ConnectionsFacade>();
builder.Services.AddHostedService<SyncScheduler>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // self-hosted: почта не обязательна
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<CashFlowDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

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

app.Run();
