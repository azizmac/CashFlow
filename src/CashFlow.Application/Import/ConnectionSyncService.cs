using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Import;

/// <summary>Синхронизация API-подключения: счета → балансы → операции (инкрементально по курсору).</summary>
public sealed class ConnectionSyncService
{
    private readonly IEnumerable<IConnector> _connectors;
    private readonly IUnitOfWork _uow;
    private readonly ISecretStore _secrets;
    private readonly TransactionImportService _import;
    private readonly ILogger<ConnectionSyncService> _log;

    public ConnectionSyncService(IEnumerable<IConnector> connectors, IUnitOfWork uow, ISecretStore secrets, TransactionImportService import, ILogger<ConnectionSyncService> log)
    {
        _connectors = connectors;
        _uow = uow;
        _secrets = secrets;
        _import = import;
        _log = log;
    }

    public IConnector? Resolve(ConnectorType type) => _connectors.FirstOrDefault(c => c.Type == type);

    public async Task<SyncRun> SyncAsync(Guid connectionId, int initialDays, CancellationToken ct)
    {
        var connection = await _uow.Connections.FindAsync(connectionId, ct) ?? throw new InvalidOperationException("Connection not found");
        var connector = Resolve(connection.ConnectorType) ?? throw new InvalidOperationException($"Connector {connection.ConnectorType} is not registered");
        if (connection.CredentialRef is null) throw new InvalidOperationException("Connection has no credentials");

        var run = new SyncRun(connection.Id);
        await _uow.SyncRuns.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        int imported = 0, skipped = 0;
        try
        {
            var secrets = await _secrets.GetAsync(connection.UserId, connection.CredentialRef, ct);
            var ctx = new ConnectionContext(connection.Id, connection.UserId, secrets, connection.SyncCursor);

            var from = connection.SyncCursor is { } cur && DateOnly.TryParse(cur, out var d) ? d.AddDays(-3) : DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-initialDays);
            var range = new DateRange(from, DateOnly.FromDateTime(DateTime.UtcNow));

            var extAccounts = await connector.GetAccountsAsync(ctx, ct);
            foreach (var ea in extAccounts)
            {
                var account = _uow.Accounts.Query().FirstOrDefault(a => a.ConnectionId == connection.Id && a.ExternalRef != null && a.ExternalRef!.ExternalId == ea.ExternalId);
                if (account is null)
                {
                    account = new Account(connection.UserId, connection.ProfileId, connection.InstitutionId, ea.Type, ea.Name, ea.Currency,
                        connection.Id, new ExternalRef(connection.ConnectorType, ea.ExternalId), ea.AccountNumber);
                    await _uow.Accounts.AddAsync(account, ct);
                    await _uow.SaveChangesAsync(ct);
                }
                if (ea.Balance is { } bal)
                {
                    await _uow.BalanceSnapshots.AddAsync(account.RecordBalance(bal, ea.Available, ea.Blocked), ct);
                }

                if (connector.Capabilities.HasFlag(ConnectorCapabilities.Transactions))
                {
                    var txs = await connector.GetTransactionsAsync(ctx, ea.ExternalId, range, ct);
                    var s = await _import.ImportAsync(connection.UserId, account, connection, connection.ConnectorType, txs, null, ct);
                    imported += s.Imported;
                    skipped += s.SkippedDuplicates;
                }
            }

            connection.MarkSynced(range.To.ToString("O"));
            run.Complete(imported, skipped);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogWarning(ex, "Connection {Id} needs re-auth", connectionId);
            connection.MarkNeedsReauth();
            run.Complete(imported, skipped, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sync failed for {Id}", connectionId);
            connection.MarkError(ex.Message);
            run.Complete(imported, skipped, ex.Message);
        }

        await _uow.SaveChangesAsync(ct);
        return run;
    }
}
