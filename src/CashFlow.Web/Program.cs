using CashFlow.Server;
using CashFlow.UI;
using CashFlow.Web.Components;
using CashFlow.Web.Components.Account;
using CashFlow.Web.Services;
using CashFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Сервер CashFlow целиком (Identity, PostgreSQL, парсеры, коннекторы, планировщик, REST) — общий с настольным приложением
builder.Services.AddCashFlowServer(builder.Configuration, withCookies: true);
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>(); // веб-вариант заглушки (RegisterConfirmation показывает ссылку)

// Веб-хост: Blazor Server + страницы Identity + общий UI
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddCashFlowUi();
builder.Services.AddScoped<CashFlow.UI.Services.ICurrentUser, CurrentUser>();
builder.Services.AddScoped<CashFlow.UI.Services.IAppShell, WebAppShell>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync(); // миграции + справочники при старте

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(CashFlow.UI.Layout.MainLayout).Assembly); // страницы общей библиотеки CashFlow.UI
app.MapAdditionalIdentityEndpoints();
app.MapBankOAuth(); // /oauth/{provider}/start|callback — подключение банка через авторизацию
app.MapCashFlowServerApi(); // /api/auth + /api/* — единственный вход для мобильного и настольного клиента
app.MapDemoWebLogin(); // /dev/login — вход демо-пользователя, работает только при заданных Demo__Email / Demo__Password

app.Run();
