using CashFlow.Client;
using CashFlow.Maui.Services;
using CashFlow.UI;
using CashFlow.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace CashFlow.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddAuthorizationCore();

        // Данные — только через REST; сессия — в защищённом хранилище платформы
        builder.Services.AddSingleton<ISessionStore, SecureSessionStore>();
        builder.Services.AddCashFlowApiClient();

#if WINDOWS || MACCATALYST
        // Настольная сборка: сервер CashFlow поднимается внутри приложения, клиент подключается к нему сам
        var server = new EmbeddedServer();
        builder.Services.AddSingleton(server);
        server.Start();
#endif

        // Абстракции хоста для общего UI
        builder.Services.AddSingleton<MauiAuthStateProvider>();
        builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<MauiAuthStateProvider>());
        builder.Services.AddCashFlowUi();
        builder.Services.AddSingleton<ICurrentUser, MauiCurrentUser>();
        builder.Services.AddSingleton<IAppShell, MauiAppShell>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
