using CashFlow.Application;
using CashFlow.Application.Import;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Shared;

namespace CashFlow.Web.Services;

/// <summary>Фоновая синхронизация всех активных API-подключений раз в N часов.</summary>
public sealed class SyncScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SyncScheduler> _log;
    private readonly TimeSpan _interval;

    public SyncScheduler(IServiceScopeFactory scopes, ILogger<SyncScheduler> log, IConfiguration config)
    {
        _scopes = scopes;
        _log = log;
        _interval = TimeSpan.FromHours(config.GetValue("Sync:IntervalHours", 6));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
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
                    .Where(c => c.LastSyncAt is null || DateTimeOffset.UtcNow - c.LastSyncAt > _interval)
                    .ToList();
                foreach (var c in due)
                {
                    _log.LogInformation("Scheduled sync {Name}", c.Name);
                    await sync.SyncAsync(c.Id, 365, ct);
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
