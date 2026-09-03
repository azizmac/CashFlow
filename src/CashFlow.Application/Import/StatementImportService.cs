using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Import;

public sealed record StatementImportResult(Account Account, ImportSummary Summary, IReadOnlyList<string> Warnings, string ParserName);

/// <summary>Импорт файла выписки: выбор парсера → поиск/создание счёта → TransactionImportService.</summary>
public sealed class StatementImportService
{
    private readonly IEnumerable<IStatementParser> _parsers;
    private readonly IUnitOfWork _uow;
    private readonly TransactionImportService _import;
    private readonly ILogger<StatementImportService> _log;

    public StatementImportService(IEnumerable<IStatementParser> parsers, IUnitOfWork uow, TransactionImportService import, ILogger<StatementImportService> log)
    {
        _parsers = parsers;
        _uow = uow;
        _import = import;
        _log = log;
    }

    public async Task<StatementImportResult> ImportAsync(string userId, Guid profileId, Stream file, string fileName, string? preferredBankCode, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        IStatementParser? parser = null;
        foreach (var p in _parsers.OrderByDescending(p => p.BankCode == preferredBankCode))
        {
            ms.Position = 0;
            if (p.Extensions.Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && await p.CanParseAsync(ms, fileName, ct))
            {
                parser = p;
                break;
            }
        }
        if (parser is null) throw new InvalidOperationException($"Не найден парсер для файла '{fileName}'. Поддерживаются: {string.Join(", ", _parsers.Select(p => $"{p.DisplayName} ({string.Join("/", p.Extensions)})"))}");

        ms.Position = 0;
        var parsed = await parser.ParseAsync(ms, fileName, ct);

        var institution = _uow.Institutions.Query().FirstOrDefault(i => i.Code == parser.BankCode)
            ?? throw new InvalidOperationException($"Institution '{parser.BankCode}' not seeded");

        var profile = await _uow.Profiles.FindAsync(profileId, ct) ?? throw new InvalidOperationException("Profile not found");
        if (profile.UserId != userId) throw new UnauthorizedAccessException();

        // Подключение типа StatementImport на банк — одно на профиль.
        var connection = _uow.Connections.Query().FirstOrDefault(c => c.UserId == userId && c.ProfileId == profileId && c.InstitutionId == institution.Id && c.ConnectorType == ConnectorType.StatementImport);
        if (connection is null)
        {
            connection = new Connection(userId, profileId, institution.Id, ConnectorType.StatementImport, $"{institution.Name} — выписки");
            await _uow.Connections.AddAsync(connection, ct);
        }

        var account = FindAccount(userId, profileId, institution.Id, parsed.Account);
        if (account is null)
        {
            account = new Account(userId, profileId, institution.Id, parsed.Account.Type, parsed.Account.Name, parsed.Account.Currency,
                connection.Id, new ExternalRef(ConnectorType.StatementImport, parsed.Account.ExternalId), parsed.Account.AccountNumber);
            await _uow.Accounts.AddAsync(account, ct);
        }
        if (parsed.Account.Balance is { } bal)
        {
            var snap = account.RecordBalance(bal, parsed.Account.Available, parsed.Account.Blocked, parsed.Period is { } p ? new DateTimeOffset(p.To.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : null);
            await _uow.BalanceSnapshots.AddAsync(snap, ct);
        }

        var run = new SyncRun(connection.Id);
        await _uow.SyncRuns.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            var summary = await _import.ImportAsync(userId, account, connection, ConnectorType.StatementImport, parsed.Transactions, fileName, ct);
            run.Complete(summary.Imported, summary.SkippedDuplicates);
            connection.MarkSynced(parsed.Period?.To.ToString("O"));
            await _uow.SaveChangesAsync(ct);
            return new StatementImportResult(account, summary, parsed.Warnings, parser.DisplayName);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Statement import failed for {File}", fileName);
            run.Complete(0, 0, ex.Message);
            connection.MarkError(ex.Message);
            await _uow.SaveChangesAsync(ct);
            throw;
        }
    }

    private Account? FindAccount(string userId, Guid profileId, Guid institutionId, ExternalAccount ext)
    {
        var candidates = _uow.Accounts.Query().Where(a => a.UserId == userId && a.ProfileId == profileId && a.InstitutionId == institutionId && !a.IsArchived).ToList();
        return candidates.FirstOrDefault(a => a.ExternalRef is { } r && r.ExternalId == ext.ExternalId)
            ?? candidates.FirstOrDefault(a => ext.AccountNumber is { Length: > 0 } && a.AccountNumber == ext.AccountNumber)
            ?? candidates.FirstOrDefault(a => ext.AccountNumber is { Length: >= 4 } n && a.AccountNumber is { Length: >= 4 } an && an.EndsWith(n[^4..]) && a.Currency == ext.Currency);
    }
}
