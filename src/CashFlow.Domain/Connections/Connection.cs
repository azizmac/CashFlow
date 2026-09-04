using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Connections;

public enum ConnectionStatus { Active = 0, NeedsReauth = 1, Disabled = 2, Error = 3 }

/// <summary>Подключение к источнику данных. Секреты хранятся отдельно (ISecretStore), здесь только ссылка.</summary>
public sealed class Connection : Entity
{
    private Connection() { }

    public Connection(string userId, Guid profileId, Guid institutionId, ConnectorType connectorType, string name)
    {
        UserId = userId;
        ProfileId = profileId;
        InstitutionId = institutionId;
        ConnectorType = connectorType;
        Name = name;
        Status = ConnectionStatus.Active;
    }

    public string UserId { get; private set; } = default!;
    public Guid ProfileId { get; private set; }
    public Guid InstitutionId { get; private set; }
    public ConnectorType ConnectorType { get; private set; }
    public string Name { get; private set; } = default!;
    /// <summary>Формат источника внутри типа: для StatementImport — код парсера (sber-card-pdf, 1c-client-bank…). Разные форматы одного банка = разные подключения.</summary>
    public string? SourceCode { get; private set; }
    public ConnectionStatus Status { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Ключ записи в ISecretStore. Сам секрет здесь никогда не лежит.</summary>
    public string? CredentialRef { get; private set; }

    public DateTimeOffset? LastSyncAt { get; private set; }
    /// <summary>Курсор инкрементальной синхронизации (дата последней операции, ID и т.п.).</summary>
    public string? SyncCursor { get; private set; }
    public DateTimeOffset? ConsentExpiresAt { get; private set; }

    public void SetSource(string? sourceCode) { SourceCode = sourceCode; Touch(); }
    public void Rename(string name) { Name = name; Touch(); }
    public void AttachCredential(string credentialRef) { CredentialRef = credentialRef; Status = ConnectionStatus.Active; LastError = null; Touch(); }
    public void MarkSynced(string? cursor) { LastSyncAt = DateTimeOffset.UtcNow; SyncCursor = cursor; Status = ConnectionStatus.Active; LastError = null; Touch(); }
    public void MarkError(string error) { Status = ConnectionStatus.Error; LastError = error; Touch(); }
    public void MarkNeedsReauth() { Status = ConnectionStatus.NeedsReauth; Touch(); }
    public void Disable() { Status = ConnectionStatus.Disabled; Touch(); }
}

public enum SyncResult { Success = 0, PartialSuccess = 1, Failed = 2 }

/// <summary>История синхронизаций.</summary>
public sealed class SyncRun : Entity
{
    private SyncRun() { }

    public SyncRun(Guid connectionId)
    {
        ConnectionId = connectionId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public Guid ConnectionId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public SyncResult? Result { get; private set; }
    public int ImportedTransactions { get; private set; }
    public int SkippedDuplicates { get; private set; }
    public string? Error { get; private set; }

    public void Complete(int imported, int skipped, string? error = null)
    {
        FinishedAt = DateTimeOffset.UtcNow;
        ImportedTransactions = imported;
        SkippedDuplicates = skipped;
        Error = error;
        Result = error is null ? SyncResult.Success : imported > 0 ? SyncResult.PartialSuccess : SyncResult.Failed;
        Touch();
    }
}

/// <summary>Сырая запись из источника — для повторной нормализации при смене парсера. Шифруется в хранилище.</summary>
public sealed class RawRecord : Entity
{
    private RawRecord() { }

    public RawRecord(Guid connectionId, ConnectorType source, string payload, string? fileName = null)
    {
        ConnectionId = connectionId;
        Source = source;
        Payload = payload;
        FileName = fileName;
    }

    public Guid ConnectionId { get; private set; }
    public ConnectorType Source { get; private set; }
    public string Payload { get; private set; } = default!;
    public string? FileName { get; private set; }
}
