using CashFlow.Application.Contracts;
using CashFlow.Application.Import;
using CashFlow.Application.Ledger;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Connections;

/// <summary>API-подключения к банкам: секреты уходят в ISecretStore, наружу — только DTO без ссылок на секреты.</summary>
public sealed class ConnectionsService : IConnectionsService
{
    private readonly IUnitOfWork _uow;
    private readonly ISecretStore _secrets;
    private readonly ConnectionSyncService _sync;
    private readonly IReadOnlyList<IConnector> _connectors;

    public ConnectionsService(IUnitOfWork uow, ISecretStore secrets, ConnectionSyncService sync, IEnumerable<IConnector> connectors)
    {
        _uow = uow;
        _secrets = secrets;
        _sync = sync;
        _connectors = connectors.ToList();
    }

    public IReadOnlyList<ConnectorInfoDto> Connectors() => _connectors
        .Where(c => !c.Capabilities.HasFlag(ConnectorCapabilities.FileImport))
        .Select(c =>
        {
            var oauth = c as IOAuthConnector;
            return new ConnectorInfoDto(c.Type, Label(c.Type), c.RequiredSecrets,
                oauth is not null, oauth?.IsConfigured ?? false,
                oauth is null ? null : c.Type.ToString().ToLowerInvariant(), oauth?.ProviderDisplayName, oauth?.SetupHint);
        }).ToList();

    public static string Label(ConnectorType t) => t switch
    {
        ConnectorType.StatementImport => "Выписки (файлы)",
        ConnectorType.TInvest => "Т-Инвестиции (T-Invest API)",
        ConnectorType.TBankBusiness => "Т-Бизнес (T-API) — р/с ИП/ЮЛ",
        ConnectorType.SberBusiness => "СберБизнес (Sber API) — р/с ИП/ЮЛ",
        ConnectorType.AlfaBusiness => "Альфа-Бизнес (Alfa API) — р/с ИП/ЮЛ",
        ConnectorType.Manual => "Вручную",
        _ => t.ToString(),
    };

    public Task<IReadOnlyList<ConnectionDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var profiles = _uow.Profiles.Query().Where(p => p.UserId == userId).ToList().ToDictionary(p => p.Id, p => p.Name);
        IReadOnlyList<ConnectionDto> list = _uow.Connections.Query().Where(c => c.UserId == userId).OrderBy(c => c.Name).ToList()
            .Select(c => c.ToDto(profiles.GetValueOrDefault(c.ProfileId, ""))).ToList();
        return Task.FromResult(list);
    }

    public async Task<IReadOnlyList<SyncRunDto>> RunsAsync(string userId, Guid connectionId, int take = 10, CancellationToken ct = default)
    {
        await OwnAsync(userId, connectionId, ct);
        IReadOnlyList<SyncRunDto> list = _uow.SyncRuns.Query().Where(s => s.ConnectionId == connectionId).OrderByDescending(s => s.StartedAt).Take(take).ToList().Select(r => r.ToDto()).ToList();
        return list;
    }

    public async Task<ConnectionDto> CreateAsync(string userId, Guid profileId, ConnectorType type, string name, IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default)
    {
        var connector = _connectors.FirstOrDefault(c => c.Type == type) ?? throw new InvalidOperationException($"Коннектор {type} не зарегистрирован");
        var missing = connector.RequiredSecrets.Where(k => !secrets.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        // OAuth-подключения хранят другой набор секретов (refresh_token вместо токена из ЛК) — для них проверка по RequiredSecrets не применяется
        if (missing.Count > 0 && connector is not IOAuthConnector) throw new InvalidOperationException("Не заполнено: " + string.Join(", ", missing));
        if (secrets.Count == 0) throw new InvalidOperationException("Не переданы реквизиты доступа");

        var profile = await _uow.Profiles.FindAsync(profileId, ct);
        if (profile is null || profile.UserId != userId) throw new UnauthorizedAccessException();

        var instCode = type switch
        {
            ConnectorType.TInvest => Institution.Codes.TInvest,
            ConnectorType.TBankBusiness => Institution.Codes.TBank,
            ConnectorType.SberBusiness => Institution.Codes.Sber,
            ConnectorType.AlfaBusiness => Institution.Codes.Alfa,
            _ => Institution.Codes.Other,
        };
        var inst = _uow.Institutions.Query().First(i => i.Code == instCode);

        var credRef = await _secrets.PutAsync(userId, secrets, ct);
        var conn = new Connection(userId, profileId, inst.Id, type, string.IsNullOrWhiteSpace(name) ? Label(type) : name.Trim());
        conn.AttachCredential(credRef);
        await _uow.Connections.AddAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return conn.ToDto(profile.Name);
    }

    public async Task<SyncRunDto> SyncAsync(string userId, Guid connectionId, int initialDays, CancellationToken ct = default)
    {
        await OwnAsync(userId, connectionId, ct);
        var run = await _sync.SyncAsync(connectionId, initialDays, ct);
        return run.ToDto();
    }

    public async Task DeleteAsync(string userId, Guid connectionId, CancellationToken ct = default)
    {
        var c = await OwnAsync(userId, connectionId, ct);
        if (c.CredentialRef is not null) await _secrets.DeleteAsync(userId, c.CredentialRef, ct);
        c.Disable();
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Connection> OwnAsync(string userId, Guid id, CancellationToken ct)
    {
        var c = await _uow.Connections.FindAsync(id, ct);
        if (c is null || c.UserId != userId) throw new UnauthorizedAccessException();
        return c;
    }
}
