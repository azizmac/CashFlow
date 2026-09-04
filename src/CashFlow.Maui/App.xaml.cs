namespace CashFlow.Maui;

public partial class App : Microsoft.Maui.Controls.Application  // полное имя: CashFlow.Application — пространство имён
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "CashFlow" };
	}
}
