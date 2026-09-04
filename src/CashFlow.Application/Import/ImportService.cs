using CashFlow.Application.Contracts;
using CashFlow.Connectors.Abstractions;

namespace CashFlow.Application.Import;

/// <summary>Фасад импорта выписок для UI и API: возвращает только DTO.</summary>
public sealed class ImportService : IImportService
{
    private readonly StatementImportService _import;
    private readonly IReadOnlyList<StatementFormatDto> _formats;

    public ImportService(StatementImportService import, IEnumerable<IStatementParser> parsers)
    {
        _import = import;
        _formats = parsers.Select(p => new StatementFormatDto(p.Code, p.BankCode, p.DisplayName, p.Extensions)).ToList();
    }

    public IReadOnlyList<StatementFormatDto> Formats => _formats;

    public async Task<ImportResultDto> ImportAsync(string userId, Guid profileId, Stream file, string fileName, string? bankHint, CancellationToken ct = default)
    {
        var r = await _import.ImportAsync(userId, profileId, file, fileName, bankHint, ct);
        return new ImportResultDto(fileName, r.ParserName, r.Account.Id, r.Account.Name, r.Connection.Id, r.Connection.Name,
            r.Period?.From, r.Period?.To,
            r.Summary.Imported, r.Summary.Updated, r.Summary.SkippedDuplicates, r.Summary.CounterpartiesCreated, r.Summary.TransfersLinked, r.Summary.Categorized,
            r.Warnings);
    }
}
