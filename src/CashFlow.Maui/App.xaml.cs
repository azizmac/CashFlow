namespace CashFlow.Maui;

public partial class App : Microsoft.Maui.Controls.Application  // полное имя: CashFlow.Application — пространство имён
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "CashFlow" };
#if WINDOWS || MACCATALYST
		// Закрытие окна — остановить встроенный API и локальный PostgreSQL, чтобы не оставлять процессов
		window.Destroying += (_, _) =>
		{
			var server = Handler?.MauiContext?.Services.GetService<Services.EmbeddedServer>();
			// Остановка идёт в фоне с лимитом времени, чтобы закрытие окна не зависало на UI-потоке
			if (server is not null) Task.Run(server.ShutdownAsync).Wait(TimeSpan.FromSeconds(20));
		};
#endif
		return window;
	}
}
