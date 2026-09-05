using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CashFlow.Api.Tests;

/// <summary>
/// Сервер CashFlow в памяти с собственной базой PostgreSQL на прогон. Все данные тестов — вымышленные.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _dbName = "cashflow_test_" + Guid.NewGuid().ToString("N")[..12];
    private string _adminCs = "";
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _adminCs = AdminConnectionString();
        await using (var conn = new NpgsqlConnection(_adminCs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        ConnectionString = new NpgsqlConnectionStringBuilder(_adminCs) { Database = _dbName }.ConnectionString;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_adminCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7 + 1)).ToArray()));
        builder.UseSetting("Demo:Email", "");
        builder.UseSetting("Demo:Password", "");
        builder.UseSetting("Sync:IntervalHours", "1000");
    }

    /// <summary>Строка подключения к служебной базе postgres: CASHFLOW_TEST_PG → .env репозитория → значения по умолчанию.</summary>
    private static string AdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("CASHFLOW_TEST_PG");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var dotenv = ReadDotEnv();
        var pwd = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? dotenv.GetValueOrDefault("POSTGRES_PASSWORD") ?? "cashflow";
        var bind = dotenv.GetValueOrDefault("DB_PORT") ?? "127.0.0.1:55432";
        var port = bind.Contains(':') ? bind[(bind.LastIndexOf(':') + 1)..] : bind;
        return $"Host=localhost;Port={port};Database=postgres;Username=cashflow;Password={pwd}";
    }

    private static Dictionary<string, string> ReadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; dir is not null && i < 10; i++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path) || !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) continue;
            foreach (var raw in File.ReadAllLines(path))
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
        return result;
    }

    // ---------- помощники для тестов ----------

    private sealed record TokenResponse(string AccessToken, string RefreshToken);

    /// <summary>Регистрирует вымышленного пользователя и возвращает клиент с bearer-токеном.</summary>
    public async Task<HttpClient> UserClientAsync(string email, string password = "Test-password-2026!")
    {
        var client = CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email, password }, Json);
        if (!reg.IsSuccessStatusCode) throw new InvalidOperationException("register: " + await reg.Content.ReadAsStringAsync());
        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=false", new { email, password }, Json);
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>(Json) ?? throw new InvalidOperationException("no token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
