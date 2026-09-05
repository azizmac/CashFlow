#if WINDOWS || MACCATALYST
using System.Security.Cryptography;
using System.Text.Json;
using CashFlow.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CashFlow.Maui.Services;

/// <summary>
/// Настольная «общая сборка»: сервер CashFlow (API, Identity, PostgreSQL) поднимается внутри приложения на localhost,
/// клиент подключается к нему автоматически — адрес вводить не нужно. На телефоне сервера нет, там указывается адрес.
///
/// Откуда берутся настройки (по убыванию приоритета):
///   1) server.json в папке данных приложения (создаётся при первом запуске);
///   2) переменные окружения ConnectionStrings__Postgres / Encryption__MasterKey / CASHFLOW_TZ;
///   3) .env рядом с docker-compose.yml, если приложение запущено из папки репозитория (POSTGRES_PASSWORD, ENCRYPTION_MASTER_KEY, DB_PORT);
///   4) значения по умолчанию: PostgreSQL из docker-compose на localhost:55432, база cashflow/cashflow, ключ шифрования генерируется и сохраняется в server.json.
/// </summary>
public sealed class EmbeddedServer
{
    public enum State { Starting, Ready, Failed }

    public sealed class Settings
    {
        public string? ConnectionString { get; set; }
        public string? MasterKey { get; set; }
        public int Port { get; set; } = 47831;
        public string? TimeZone { get; set; }
    }

    private static readonly (string Key, string Env)[] IntegrationKeys =
    [
        ("Integrations:PublicBaseUrl", "PUBLIC_BASE_URL"),
        ("Integrations:Sber:ClientId", "SBER_CLIENT_ID"), ("Integrations:Sber:ClientSecret", "SBER_CLIENT_SECRET"),
        ("Integrations:Sber:CertPfxPath", "SBER_CERT_PFX_PATH"), ("Integrations:Sber:CertPassword", "SBER_CERT_PASSWORD"),
        ("Integrations:Alfa:ClientId", "ALFA_CLIENT_ID"), ("Integrations:Alfa:ClientSecret", "ALFA_CLIENT_SECRET"),
        ("Integrations:TBank:ClientId", "TBANK_CLIENT_ID"), ("Integrations:TBank:ClientSecret", "TBANK_CLIENT_SECRET"),
    ];

    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WebApplication? _app;

    public State Status { get; private set; } = State.Starting;
    public string? Error { get; private set; }
    public string? BaseUrl { get; private set; }
    public string DataDirectory { get; } = Path.Combine(FileSystem.AppDataDirectory, "server");
    public string SettingsPath => Path.Combine(DataDirectory, "server.json");
    public string LogPath => Path.Combine(DataDirectory, "server.log");
    public Settings Current { get; private set; } = new();
    /// <summary>Завершается, когда сервер готов или запуск не удался (никогда не бросает).</summary>
    public Task Started => _started.Task;
    public event Action? Changed;

    public bool IsLocalUrl(string? url) => url is not null && BaseUrl is not null &&
        (url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase));

    public void Start() => _ = Task.Run(StartCoreAsync);

    public async Task RestartAsync()
    {
        Status = State.Starting; Error = null; Changed?.Invoke();
        if (_app is not null) { try { await _app.StopAsync(); } catch { } _app = null; }
        await StartCoreAsync();
    }

    public async Task SaveSettingsAsync(Settings s)
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        Current = s;
    }

    private async Task StartCoreAsync()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var s = await LoadSettingsAsync();
            var config = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = s.ConnectionString,
                ["Encryption:MasterKey"] = s.MasterKey,
                ["Sync:IntervalHours"] = "6",
                ["Logging:LogLevel:Default"] = "Warning",
            };
            if (!string.IsNullOrWhiteSpace(s.TimeZone)) Environment.SetEnvironmentVariable("CASHFLOW_TZ", s.TimeZone);
            // Демо-пользователь (локальные проверки): из .env репозитория или окружения, как у контейнера
            var env = ReadDotEnv();
            config["Demo:Email"] = Environment.GetEnvironmentVariable("DEMO_EMAIL") ?? env.GetValueOrDefault("DEMO_EMAIL");
            config["Demo:Password"] = Environment.GetEnvironmentVariable("DEMO_PASSWORD") ?? env.GetValueOrDefault("DEMO_PASSWORD");
            // Реквизиты OAuth-приложений банков — те же ключи .env, что у docker-compose; redirect URI = http://127.0.0.1:{порт}/oauth/{provider}/callback
            foreach (var (key, envName) in IntegrationKeys)
                config[key] = Environment.GetEnvironmentVariable(envName) ?? env.GetValueOrDefault(envName);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "CashFlow.Server",
                ContentRootPath = DataDirectory,
                WebRootPath = DataDirectory,
                EnvironmentName = "Production",
            });
            builder.Configuration.AddInMemoryCollection(config);
            builder.Configuration.AddEnvironmentVariables();
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Logging.AddProvider(new FileLoggerProvider(LogPath)); // server.log в папке данных: сюда падают ошибки сервера
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddCashFlowServer(builder.Configuration, withCookies: false);
            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(DataDirectory, "keys"))); // токены переживают перезапуск
            builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64L * 1024 * 1024);

            var app = builder.Build();
            app.MapCashFlowServerApi();

            var port = await FindFreePortAsync(s.Port);
            app.Urls.Add($"http://127.0.0.1:{port}");
            await app.StartAsync();
            _app = app;
            BaseUrl = $"http://127.0.0.1:{port}";

            await app.Services.InitializeDatabaseAsync();
            Status = State.Ready; Error = null;
        }
        catch (Exception ex)
        {
            Status = State.Failed;
            Error = Describe(ex);
            if (_app is not null) { try { await _app.StopAsync(); } catch { } _app = null; }
        }
        finally
        {
            _started.TrySetResult();
            Changed?.Invoke();
        }
    }

    private async Task<Settings> LoadSettingsAsync()
    {
        Settings s = new();
        if (File.Exists(SettingsPath))
        {
            try { s = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(SettingsPath)) ?? new(); } catch { s = new(); }
        }
        var env = ReadDotEnv();
        var changed = false;

        if (string.IsNullOrWhiteSpace(s.ConnectionString))
        {
            s.ConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
            if (string.IsNullOrWhiteSpace(s.ConnectionString))
            {
                var pwd = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? env.GetValueOrDefault("POSTGRES_PASSWORD") ?? "cashflow";
                var bind = env.GetValueOrDefault("DB_PORT") ?? "127.0.0.1:55432"; // порт из docker-compose.yml: 5432 на машине часто занят своим PostgreSQL
                var port = bind.Contains(':') ? bind[(bind.LastIndexOf(':') + 1)..] : bind;
                s.ConnectionString = $"Host=localhost;Port={port};Database=cashflow;Username=cashflow;Password={pwd}";
            }
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(s.MasterKey))
        {
            s.MasterKey = Environment.GetEnvironmentVariable("Encryption__MasterKey") ?? Environment.GetEnvironmentVariable("ENCRYPTION_MASTER_KEY") ?? env.GetValueOrDefault("ENCRYPTION_MASTER_KEY");
            if (string.IsNullOrWhiteSpace(s.MasterKey)) s.MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(s.TimeZone))
        {
            s.TimeZone = Environment.GetEnvironmentVariable("CASHFLOW_TZ") ?? env.GetValueOrDefault("CASHFLOW_TZ");
            changed = s.TimeZone is not null || changed;
        }
        if (changed) await SaveSettingsAsync(s);
        Current = s;
        return s;
    }

    /// <summary>.env рядом с docker-compose.yml, если exe лежит внутри папки репозитория (разработка): те же пароль и ключ, что у контейнера.</summary>
    private static Dictionary<string, string> ReadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; dir is not null && i < 10; i++, dir = dir.Parent)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (!File.Exists(envPath) || !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) continue;
                foreach (var raw in File.ReadAllLines(envPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var value = line[(eq + 1)..].Trim().Trim('"', '\'');
                    if (value.Length > 0) result[line[..eq].Trim()] = value;
                }
                break;
            }
        }
        catch { }
        return result;
    }

    private static async Task<int> FindFreePortAsync(int preferred)
    {
        for (var p = preferred; p < preferred + 20; p++)
        {
            try
            {
                using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, p);
                l.Start(); l.Stop();
                return p;
            }
            catch { await Task.Yield(); }
        }
        return 0;
    }

    private string Describe(Exception ex)
    {
        var root = ex; while (root.InnerException is not null) root = root.InnerException;
        var host = Current.ConnectionString?.Split(';').FirstOrDefault(p => p.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))?[5..] ?? "localhost";
        return root switch
        {
            System.Net.Sockets.SocketException => $"PostgreSQL на {host} не отвечает. Запустите базу (в папке проекта: docker compose up -d db) или укажите другие параметры подключения ниже.",
            Npgsql.PostgresException pg when pg.SqlState == "28P01" => "PostgreSQL отклонил пароль. Проверьте параметры подключения ниже.",
            Npgsql.PostgresException pg when pg.SqlState == "3D000" => "База данных cashflow не найдена на сервере PostgreSQL. Проверьте название базы.",
            Npgsql.PostgresException pg => $"Ошибка PostgreSQL: {pg.MessageText}",
            System.Security.Cryptography.AuthenticationTagMismatchException or CryptographicException =>
                "Ключ шифрования не подходит к данным в базе. Если база создана веб-версией, укажите тот же ENCRYPTION_MASTER_KEY в server.json.",
            _ => $"Не удалось запустить сервер: {root.Message}",
        };
    }
}

/// <summary>Минимальный файловый логгер: одна строка на запись, файл обрезается при старте, если вырос больше 2 МБ.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        try { if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Delete(path); } catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private void Write(string line)
    {
        lock (_lock) { try { File.AppendAllText(_path, line + Environment.NewLine); } catch { } }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
        }
    }
}
#endif
