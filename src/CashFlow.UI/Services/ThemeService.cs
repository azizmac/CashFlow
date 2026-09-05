using Microsoft.JSInterop;

namespace CashFlow.UI.Services;

/// <summary>
/// Тема оформления: тёмная (базовая), светлая или системная. Хранится в localStorage хоста
/// (работает и в браузере, и в WebView MAUI), применяется атрибутом data-theme на html.
/// </summary>
public sealed class ThemeService
{
    public const string Dark = "dark", Light = "light", System = "system";
    private readonly IJSRuntime _js;
    public ThemeService(IJSRuntime js) => _js = js;

    public string Current { get; private set; } = System;
    public event Action? Changed;

    public async Task ApplyStoredAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("eval", "localStorage.getItem('cashflow.theme')");
            Current = stored is Dark or Light ? stored : System;
            await ApplyAsync(Current);
        }
        catch { /* пререндер без JS — применится после подключения */ }
    }

    public async Task SetAsync(string theme)
    {
        Current = theme is Dark or Light ? theme : System;
        try
        {
            await _js.InvokeVoidAsync("eval", $"localStorage.setItem('cashflow.theme','{Current}')");
            await ApplyAsync(Current);
        }
        catch { }
        Changed?.Invoke();
    }

    private Task ApplyAsync(string theme)
    {
        // system: атрибут снимаем, тему выбирает prefers-color-scheme (см. inline-скрипт в index.html / App.razor)
        var script = theme == System
            ? "document.documentElement.dataset.theme = window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'; document.documentElement.dataset.themeMode='system';"
            : $"document.documentElement.dataset.theme='{theme}'; document.documentElement.dataset.themeMode='{theme}';";
        return _js.InvokeVoidAsync("eval", script).AsTask();
    }
}
