#if WINDOWS || MACCATALYST
using System.Diagnostics;
using System.Security.Cryptography;

namespace CashFlow.Maui.Services;

/// <summary>
/// Локальный PostgreSQL для настольной сборки без Docker: бинарники лежат рядом с приложением (`pgsql/bin`, кладёт установщик)
/// или по пути из server.json, кластер — в папке данных приложения (`server/pgdata`). При первом запуске делается initdb
/// с пользователем cashflow и сгенерированным паролем, дальше pg_ctl start/stop вместе с приложением.
/// Слушает только 127.0.0.1.
/// </summary>
public sealed class EmbeddedPostgres
{
    public const string User = "cashflow";
    public const string Database = "cashflow";

    private readonly string _binDir;
    private readonly string _dataDir;
    private readonly string _logPath;
    private readonly int _port;

    public EmbeddedPostgres(string binDir, string dataDir, int port)
    {
        _binDir = binDir;
        _dataDir = dataDir;
        _logPath = Path.Combine(Path.GetDirectoryName(dataDir)!, "postgres.log");
        _port = port;
    }

    public static bool IsAvailable(string? binDir) => binDir is not null && File.Exists(Path.Combine(binDir, "pg_ctl.exe"));

    /// <summary>Где искать бинарники: явный путь из настроек → pgsql/bin рядом с exe → pgsql/bin в папке данных.</summary>
    public static string? Locate(string? configured, string dataDirectory)
    {
        foreach (var dir in new[] { configured, Path.Combine(AppContext.BaseDirectory, "pgsql", "bin"), Path.Combine(dataDirectory, "pgsql", "bin") })
            if (IsAvailable(dir)) return dir;
        return null;
    }

    public string ConnectionString(string password) => $"Host=127.0.0.1;Port={_port};Database={Database};Username={User};Password={password}";

    public static string GeneratePassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'a').Replace('/', 'b').Replace("=", "");

    /// <summary>Создаёт кластер при первом запуске (initdb с этим паролем) и стартует сервер, если он ещё не запущен.</summary>
    public async Task EnsureStartedAsync(string password, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dataDir)!);
        if (!File.Exists(Path.Combine(_dataDir, "PG_VERSION")))
        {
            if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); // недоделанный initdb с прошлого раза
            var pwFile = Path.Combine(Path.GetDirectoryName(_dataDir)!, "pg-init.tmp");
            await File.WriteAllTextAsync(pwFile, password, ct);
            try
            {
                await RunAsync("initdb.exe", $"-D \"{_dataDir}\" -U {User} --pwfile=\"{pwFile}\" --auth=scram-sha-256 -E UTF8 --locale=C", ct);
            }
            finally { File.Delete(pwFile); }
        }

        if (!await IsRunningAsync(ct))
        {
            // Без перенаправления вывода: сервер postgres наследует потоки pg_ctl, и чтение stdout никогда бы не завершилось
            var (code, _) = await RunRawAsync("pg_ctl.exe", $"-D \"{_dataDir}\" -l \"{_logPath}\" -w -t 60 -o \"-p {_port} -c listen_addresses=127.0.0.1\" start", ct, redirect: false);
            if (code != 0) throw new InvalidOperationException($"pg_ctl start завершился с кодом {code}, подробности в {_logPath}");
        }
        await EnsureDatabaseAsync(password, ct);
    }

    public async Task StopAsync()
    {
        try { if (await IsRunningAsync(default)) await RunAsync("pg_ctl.exe", $"-D \"{_dataDir}\" -m fast -w -t 30 stop", default); }
        catch { /* при выходе из приложения ошибки остановки не важны */ }
    }

    private async Task<bool> IsRunningAsync(CancellationToken ct)
    {
        var (code, _) = await RunRawAsync("pg_ctl.exe", $"-D \"{_dataDir}\" status", ct);
        return code == 0; // 3 = не запущен, 4 = нет каталога
    }

    private async Task EnsureDatabaseAsync(string password, CancellationToken ct)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString(password)) { Database = "postgres" };
        await using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var check = new Npgsql.NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        check.Parameters.AddWithValue("n", Database);
        if (await check.ExecuteScalarAsync(ct) is null)
        {
            await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{Database}\" OWNER \"{User}\"", conn);
            await create.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task RunAsync(string exe, string args, CancellationToken ct)
    {
        var (code, output) = await RunRawAsync(exe, args, ct);
        if (code != 0) throw new InvalidOperationException($"{exe} завершился с кодом {code}: {output.Trim()}");
    }

    private async Task<(int Code, string Output)> RunRawAsync(string exe, string args, CancellationToken ct, bool redirect = true)
    {
        var psi = new ProcessStartInfo(Path.Combine(_binDir, exe), args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = redirect, RedirectStandardError = redirect,
            WorkingDirectory = _binDir,
        };
        psi.Environment["PGCLIENTENCODING"] = "UTF8";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {exe}");
        if (!redirect) { await p.WaitForExitAsync(ct); return (p.ExitCode, ""); }
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdout + await stderr);
    }
}
#endif
