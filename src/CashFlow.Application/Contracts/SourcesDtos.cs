using CashFlow.Domain.Connections;
using CashFlow.Domain.Shared;

namespace CashFlow.Application.Contracts;

public sealed record ConnectionDto(
    Guid Id, Guid ProfileId, string ProfileName, string Name, ConnectorType Type, string? SourceCode,
    ConnectionStatus Status, string? LastError, DateTimeOffset? LastSyncAt, string? SyncCursor)
{
    public bool IsStatementImport => Type == ConnectorType.StatementImport;
}

public sealed record SyncRunDto(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, SyncResult? Result, int Imported, int Skipped, string? Error);

/// <summary>Описание коннектора для формы подключения. OAuthProviderKey — сегмент URL /oauth/{key}/start.</summary>
public sealed record ConnectorInfoDto(
    ConnectorType Type, string Label, IReadOnlyList<string> RequiredSecrets,
    bool SupportsOAuth, bool OAuthConfigured, string? OAuthProviderKey, string? ProviderDisplayName, string? SetupHint);

public sealed record StatementFormatDto(string Code, string BankCode, string DisplayName, IReadOnlyList<string> Extensions);

public sealed record ImportResultDto(
    string FileName, string ParserName, Guid AccountId, string AccountName, Guid ConnectionId, string ConnectionName,
    DateOnly? PeriodFrom, DateOnly? PeriodTo,
    int Imported, int Updated, int SkippedDuplicates, int CounterpartiesCreated, int TransfersLinked, int Categorized,
    IReadOnlyList<string> Warnings);
