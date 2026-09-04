using CashFlow.Application;
using CashFlow.Application.Import;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Infrastructure.Sync;

public sealed class SyncOptions
{
    public const string Section = "Sync";
    public int IntervalHours { get; set; } = 6;
    public int InitialDays { get; set; } = 365;
}

/// <summary>Фоновая синхронизация всех активных API-подключений раз в N часов. Только на сервере.</summary>
public sealed class SyncScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SyncScheduler> _log;
    private readonly SyncOptions _opt;

    public SyncScheduler(IServiceScopeFactory scopes, ILogger<SyncScheduler> log, IOptions<SyncOptions> options)
    {
        _scopes = scopes;
        _log = log;
        _opt = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        var interval = TimeSpan.FromHours(Math.Max(1, _opt.IntervalHours));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var sync = scope.ServiceProvider.GetRequiredService<ConnectionSyncService>();
                var due = uow.Connections.Query()
                    .Where(c => c.Status == ConnectionStatus.Active && c.ConnectorType != ConnectorType.StatementImport && c.ConnectorType != ConnectorType.Manual && c.CredentialRef != null)
                    .ToList()
                    .Where(c => c.LastSyncAt is null || DateTimeOffset.UtcNow - c.LastSyncAt > interval)
                    .ToList();
                foreach (var c in due)
                {
                    _log.LogInformation("Scheduled sync {Name}", c.Name);
                    await sync.SyncAsync(c.Id, _opt.InitialDays, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Scheduled sync failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }
}
