namespace CashFlow.Client;

/// <summary>Сохранённая сессия: адрес сервера и токены Identity. Хранится в защищённом хранилище платформы.</summary>
public sealed record SessionData(string BaseUrl, string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>Платформенное хранилище сессии (SecureStorage в MAUI, файл в тестах).</summary>
public interface ISessionStore
{
    Task<SessionData?> LoadAsync();
    Task SaveAsync(SessionData data);
    Task ClearAsync();
}

/// <summary>Текущая сессия клиента в памяти. Меняется при входе, обновлении токена и выходе.</summary>
public sealed class ApiSession
{
    private readonly ISessionStore _store;
    private SessionData? _data;

    public ApiSession(ISessionStore store) => _store = store;

    public SessionData? Current => _data;
    public bool IsAuthenticated => _data is not null;
    public string? BaseUrl => _data?.BaseUrl;
    public event Action? Changed;

    public async Task RestoreAsync()
    {
        _data = await _store.LoadAsync();
        Changed?.Invoke();
    }

    public async Task SetAsync(SessionData data)
    {
        _data = data;
        await _store.SaveAsync(data);
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        _data = null;
        await _store.ClearAsync();
        Changed?.Invoke();
    }

    /// <summary>Абсолютный URL на сервере по относительному пути API.</summary>
    public Uri Url(string path)
    {
        var b = BaseUrl ?? throw new InvalidOperationException("Адрес сервера не задан");
        return new Uri(new Uri(b.TrimEnd('/') + "/"), path.TrimStart('/'));
    }
}

/// <summary>Ошибка API с сообщением для пользователя (400/403/404 от сервера).</summary>
public sealed class ApiException : InvalidOperationException
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
