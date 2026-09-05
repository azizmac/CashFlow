using Microsoft.Extensions.Configuration;

namespace CashFlow.Server;

/// <summary>
/// Разработка без ручной настройки: если процесс запущен из папки репозитория (Rider, `dotnet run`), берём `.env` рядом
/// с docker-compose.yml и переводим его ключи в конфигурацию сервера — те же пароль базы, ключ шифрования и демо-пользователь,
/// что у контейнера. Тогда веб из IDE, контейнер и настольное приложение работают с одной базой и одним ключом.
/// Переменные окружения, добавленные позже, имеют приоритет.
/// </summary>
public static class DotEnvConfiguration
{
    private static readonly (string Key, string Env)[] Map =
    [
        ("Encryption:MasterKey", "ENCRYPTION_MASTER_KEY"),
        ("Demo:Email", "DEMO_EMAIL"), ("Demo:Password", "DEMO_PASSWORD"),
        ("Sync:IntervalHours", "SYNC_INTERVAL_HOURS"),
        ("Integrations:PublicBaseUrl", "PUBLIC_BASE_URL"),
        ("Integrations:Sber:ClientId", "SBER_CLIENT_ID"), ("Integrations:Sber:ClientSecret", "SBER_CLIENT_SECRET"),
        ("Integrations:Sber:CertPfxPath", "SBER_CERT_PFX_PATH"), ("Integrations:Sber:CertPassword", "SBER_CERT_PASSWORD"),
        ("Integrations:Alfa:ClientId", "ALFA_CLIENT_ID"), ("Integrations:Alfa:ClientSecret", "ALFA_CLIENT_SECRET"),
        ("Integrations:TBank:ClientId", "TBANK_CLIENT_ID"), ("Integrations:TBank:ClientSecret", "TBANK_CLIENT_SECRET"),
    ];

    /// <summary>Добавляет значения из `.env` репозитория (если найден). Возвращает путь к файлу или null.</summary>
    public static string? AddCashFlowDotEnv(this IConfigurationBuilder builder, string? startDirectory = null)
    {
        var path = FindDotEnv(startDirectory ?? AppContext.BaseDirectory) ?? FindDotEnv(Directory.GetCurrentDirectory());
        if (path is null) return null;
        var env = Parse(path);
        var values = new Dictionary<string, string?>();
        foreach (var (key, name) in Map)
            if (env.TryGetValue(name, out var v) && v.Length > 0) values[key] = v;

        if (env.TryGetValue("POSTGRES_PASSWORD", out var pwd) && pwd.Length > 0)
        {
            var bind = env.GetValueOrDefault("DB_PORT") ?? "127.0.0.1:55432";
            var port = bind.Contains(':') ? bind[(bind.LastIndexOf(':') + 1)..] : bind;
            values["ConnectionStrings:Postgres"] = $"Host=localhost;Port={port};Database=cashflow;Username=cashflow;Password={pwd}";
        }
        if (env.TryGetValue("CASHFLOW_TZ", out var tz) && tz.Length > 0 && Environment.GetEnvironmentVariable("CASHFLOW_TZ") is null)
            Environment.SetEnvironmentVariable("CASHFLOW_TZ", tz);

        builder.AddInMemoryCollection(values);
        return path;
    }

    /// <summary>Пары KEY=VALUE из .env; комментарии и пустые строки пропускаются, кавычки снимаются.</summary>
    public static Dictionary<string, string> Parse(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var value = line[(eq + 1)..].Trim().Trim('"', '\'');
            if (value.Length > 0) result[line[..eq].Trim()] = value;
        }
        return result;
    }

    private static string? FindDotEnv(string start)
    {
        try
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; dir is not null && i < 10; i++, dir = dir.Parent)
            {
                var env = Path.Combine(dir.FullName, ".env");
                if (File.Exists(env) && File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) return env;
            }
        }
        catch { }
        return null;
    }
}
