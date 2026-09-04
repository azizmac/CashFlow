using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Import;

public sealed record StatementImportResult(Account Account, Connection Connection, ImportSummary Summary, IReadOnlyList<string> Warnings, string ParserName, DateRange? Period);

/// <summary>
/// Импорт файла выписки: выбор парсера → банк → подключение (одно на профиль × банк × формат) → счёт → TransactionImportService.
///
/// Механика:
///  • Подключение (Connection) для выписок создаётся на каждую комбинацию профиль + банк + формат (SourceCode = IStatementParser.Code).
///    Так PDF по карте Сбера и 1С-выгрузка р/с СберБизнеса живут в разных подключениях с раздельной историей импортов.
///  • Счёт (Account) ищется по всему профилю и банку, а не только внутри подключения: одна и та же 20-значная запись
///    из XLSX, 1С и Sber API попадает в один счёт. API-подключение потом «усыновляет» такой счёт (см. ConnectionSyncService).
///  • Если несколько парсеров узнают файл, берётся тот, который вернул операции.
/// </summary>
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

        var (parser, parsed) = await ParseWithBestParserAsync(ms, fileName, preferredBankCode, ct);

        var bankCode = parsed.DetectedBankCode ?? parser.BankCode;
        var institution = _uow.Institutions.Query().FirstOrDefault(i => i.Code == bankCode)
            ?? _uow.Institutions.Query().FirstOrDefault(i => i.Code == Institution.Codes.Other)
            ?? throw new InvalidOperationException($"Institution '{bankCode}' not seeded");

        var profile = await _uow.Profiles.FindAsync(profileId, ct) ?? throw new InvalidOperationException("Profile not found");
        if (profile.UserId != userId) throw new UnauthorizedAccessException();

        var connection = ResolveConnection(userId, profileId, institution, parser);
        var account = FindAccount(userId, profileId, institution.Id, parsed.Account);
        if (account is null)
        {
            account = new Account(userId, profileId, institution.Id, parsed.Account.Type, parsed.Account.Name, parsed.Account.Currency,
                connection.Id, new ExternalRef(ConnectorType.StatementImport, parsed.Account.ExternalId), parsed.Account.AccountNumber);
            await _uow.Accounts.AddAsync(account, ct);
        }
        if (parsed.Account.Balance is { } bal && bal.Currency == account.Currency)
        {
            var at = parsed.Period is { } p ? new DateTimeOffset(p.To.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : (DateTimeOffset?)null;
            // Не откатываем свежий баланс более старой выпиской
            if (account.LastBalanceAt is null || at is null || at >= account.LastBalanceAt)
                await _uow.BalanceSnapshots.AddAsync(account.RecordBalance(bal, parsed.Account.Available, parsed.Account.Blocked, at), ct);
        }

        var run = new SyncRun(connection.Id);
        await _uow.SyncRuns.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            var summary = await _import.ImportAsync(userId, account, connection, ConnectorType.StatementImport, parsed.Transactions, fileName, ct);
            run.Complete(summary.Imported, summary.SkippedDuplicates);
            var cursor = parsed.Period is { } pp && (connection.SyncCursor is null || !DateOnly.TryParse(connection.SyncCursor, out var prev) || pp.To > prev) ? pp.To.ToString("O") : connection.SyncCursor;
            connection.MarkSynced(cursor);
            await _uow.SaveChangesAsync(ct);
            return new StatementImportResult(account, connection, summary, parsed.Warnings, parser.DisplayName, parsed.Period);
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

    /// <summary>Пробует парсеры по очереди; первый, вернувший операции, побеждает. Если операций нет ни у кого — результат первого подходящего (с его предупреждениями).</summary>
    private async Task<(IStatementParser Parser, StatementParseResult Result)> ParseWithBestParserAsync(MemoryStream ms, string fileName, string? preferredBankCode, CancellationToken ct)
    {
        var candidates = new List<IStatementParser>();
        foreach (var p in _parsers.OrderByDescending(p => p.BankCode == preferredBankCode))
        {
            if (!p.Extensions.Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
            ms.Position = 0;
            if (await p.CanParseAsync(ms, fileName, ct)) candidates.Add(p);
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException($"Не найден парсер для файла '{fileName}'. Поддерживаются: {string.Join("; ", _parsers.Select(p => $"{p.DisplayName} ({string.Join("/", p.Extensions)})"))}");

        (IStatementParser, StatementParseResult)? first = null;
        Exception? lastError = null;
        foreach (var p in candidates)
        {
            try
            {
                ms.Position = 0;
                var r = await p.ParseAsync(ms, fileName, ct);
                if (r.Transactions.Count > 0) return (p, r);
                first ??= (p, r);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Parser {Parser} failed on {File}", p.Code, fileName);
                lastError = ex;
            }
        }
        return first ?? throw new InvalidOperationException($"Не удалось разобрать файл '{fileName}'", lastError);
    }

    private Connection ResolveConnection(string userId, Guid profileId, Institution institution, IStatementParser parser)
    {
        var all = _uow.Connections.Query()
            .Where(c => c.UserId == userId && c.ProfileId == profileId && c.InstitutionId == institution.Id && c.ConnectorType == ConnectorType.StatementImport)
            .ToList();

        var connection = all.FirstOrDefault(c => c.SourceCode == parser.Code);
        if (connection is not null) return connection;

        // Подключение, созданное до появления SourceCode («Сбер — выписки»), привязываем к первому же формату
        var legacy = all.FirstOrDefault(c => c.SourceCode is null);
        if (legacy is not null)
        {
            legacy.SetSource(parser.Code);
            legacy.Rename($"{institution.Name}: {parser.DisplayName}");
            return legacy;
        }

        connection = new Connection(userId, profileId, institution.Id, ConnectorType.StatementImport, $"{institution.Name}: {parser.DisplayName}");
        connection.SetSource(parser.Code);
        _uow.Connections.AddAsync(connection).GetAwaiter().GetResult();
        return connection;
    }

    private Account? FindAccount(string userId, Guid profileId, Guid institutionId, ExternalAccount ext)
    {
        var candidates = _uow.Accounts.Query().Where(a => a.UserId == userId && a.ProfileId == profileId && a.InstitutionId == institutionId && !a.IsArchived).ToList();
        var byExt = candidates.FirstOrDefault(a => a.ExternalRef is { } r && r.Connector == ConnectorType.StatementImport && r.ExternalId == ext.ExternalId);
        if (byExt is not null) return byExt;

        // Полный номер счёта — самый надёжный ключ между форматами и API
        if (ext.AccountNumber is { Length: >= 16 } full)
            return candidates.FirstOrDefault(a => a.AccountNumber == full);

        // Маскированная карта («****1234»): совпадение по последним 4 цифрам, только среди карт той же валюты
        if (ext.AccountNumber is { Length: >= 4 } masked)
        {
            var last4 = masked[^4..];
            return candidates.FirstOrDefault(a => a.Type == ext.Type && a.Currency == ext.Currency && a.AccountNumber is { Length: >= 4 } an && an.EndsWith(last4)
                                                  && (an.Length < 16 || ext.Type == AccountType.Card));
        }
        return null;
    }
}
