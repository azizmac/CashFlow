using CashFlow.Application;
using CashFlow.Application.Import;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Shared;

namespace CashFlow.Web.Services;

/// <summary>Создание API-подключений: секреты → ISecretStore, в домене только ссылка.</summary>
public sealed class ConnectionsFacade
{
    private readonly IUnitOfWork _uow;
    private readonly ISecretStore _secrets;
    private readonly ConnectionSyncService _sync;
    private readonly IEnumerable<IConnector> _connectors;

    public ConnectionsFacade(IUnitOfWork uow, ISecretStore secrets, ConnectionSyncService sync, IEnumerable<IConnector> connectors)
    {
        _uow = uow;
        _secrets = secrets;
        _sync = sync;
        _connectors = connectors;
    }

    public IReadOnlyList<IConnector> ApiConnectors => _connectors.Where(c => !c.Capabilities.HasFlag(ConnectorCapabilities.FileImport)).ToList();

    public async Task<Connection> CreateAsync(string userId, Guid profileId, ConnectorType type, string name, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        var connector = _connectors.First(c => c.Type == type);
        var missing = connector.RequiredSecrets.Where(k => !secrets.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("Не заполнено: " + string.Join(", ", missing));

        var instCode = type switch
        {
            ConnectorType.TInvest => Institution.Codes.TInvest,
            ConnectorType.TBankBusiness => Institution.Codes.TBank,
            ConnectorType.SberBusiness => Institution.Codes.Sber,
            _ => Institution.Codes.Other,
        };
        var inst = _uow.Institutions.Query().First(i => i.Code == instCode);

        var credRef = await _secrets.PutAsync(userId, secrets, ct);
        var conn = new Connection(userId, profileId, inst.Id, type, name);
        conn.AttachCredential(credRef);
        await _uow.Connections.AddAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return conn;
    }

    public Task<SyncRun> SyncNowAsync(Guid connectionId, int initialDays, CancellationToken ct) => _sync.SyncAsync(connectionId, initialDays, ct);

    public async Task DeleteAsync(string userId, Guid connectionId, CancellationToken ct)
    {
        var c = await _uow.Connections.FindAsync(connectionId, ct);
        if (c is null || c.UserId != userId) return;
        if (c.CredentialRef is not null) await _secrets.DeleteAsync(userId, c.CredentialRef, ct);
        c.Disable();
        await _uow.SaveChangesAsync(ct);
    }
}
