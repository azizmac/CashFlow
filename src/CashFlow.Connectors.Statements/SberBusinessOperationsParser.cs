using System.Globalization;
using System.Text.RegularExpressions;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using ClosedXML.Excel;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// СберБизнес → раздел «Операции» → экспорт в XLSX («СберБизнес_Операции_за_…xlsx»). Проверено на реальном файле.
/// Колонки: Номер | Номер счёта | Дата | Контрагент счёт | Контрагент | Поступление | Валюта | Списание | Валюта | Назначение.
/// Есть названия контрагентов и назначение платежа, нет ИНН, БИК и остатков. Номера счетов с точками: «40802.810.7.00000000321».
/// Номер документа тот же, что в выписке и 1С, поэтому операции из этого файла дедуплицируются с другими форматами
/// и обогащают краткую выписку контрагентами.
/// </summary>
public sealed partial class SberBusinessOperationsParser : IStatementParser
{
    public string BankCode => Institution.Codes.Sber;
    public string Code => "sber-business-ops";
    public string DisplayName => "СберБизнес: экспорт операций (XLSX с контрагентами)";
    public IReadOnlyList<string> Extensions => [".xlsx"];

    [GeneratedRegex(@"(?<y1>\d{4})[_.-](?<m1>\d{2})[_.-](?<d1>\d{2})[_.-]+(?<y2>\d{4})[_.-](?<m2>\d{2})[_.-](?<d2>\d{2})", RegexOptions.Compiled)]
    private static partial Regex PeriodInFileName();

    private static readonly string[] RequiredHeaders = ["дата", "контрагент", "поступление", "списание", "назначение"];

    public Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        try
        {
            using var wb = new XLWorkbook(content);
            var ws = wb.Worksheets.First();
            var header = ws.Row(1).CellsUsed().Select(c => TextNormalizer.Normalize(c.GetString())).ToHashSet();
            return Task.FromResult(RequiredHeaders.All(header.Contains));
        }
        catch { return Task.FromResult(false); }
    }

    public Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var wb = new XLWorkbook(content);
        var ws = wb.Worksheets.First();
        var warnings = new List<string>();

        // Колонка «Валюта» встречается дважды (для поступления и списания) — берём первую
        var header = ws.Row(1).CellsUsed()
            .Select(c => (Name: TextNormalizer.Normalize(c.GetString()), Col: c.Address.ColumnNumber))
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Min(x => x.Col));
        int Col(string name) => header.TryGetValue(name, out var i) ? i : -1;
        // В реальном файле заголовок «Контрагент cчёт» набран с латинской «c», поэтому колонку счёта контрагента ищем по началу слова
        int ColStartsWith(string prefix, string exclude) => header.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key != exclude).Select(kv => kv.Value).DefaultIfEmpty(-1).First();
        int cDoc = Col("номер"), cOwn = ColStartsWith("номер сч", "номер"), cDate = Col("дата"), cCpAcc = ColStartsWith("контрагент", "контрагент"), cCp = Col("контрагент"),
            cCredit = Col("поступление"), cDebit = Col("списание"), cPurpose = Col("назначение"), cCurrency = Col("валюта");
        if (cDate < 0 || cCredit < 0 || cDebit < 0) throw new InvalidDataException("Не найдены колонки Дата / Поступление / Списание");

        string? ownAccount = null;
        var currency = Currency.RUB;
        var txs = new List<ExternalTransaction>();
        var seenIds = new HashSet<string>();
        DateOnly? min = null, max = null;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            string Cell(int c) => c < 0 ? "" : row.Cell(c).Value.IsDateTime ? row.Cell(c).Value.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : row.Cell(c).GetString().Trim();
            decimal? Num(int c) => c < 0 ? null : row.Cell(c).Value.IsNumber ? (decimal)row.Cell(c).Value.GetNumber() : TryDec(row.Cell(c).GetString());

            if (!DateOnly.TryParseExact(Cell(cDate), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            ownAccount ??= Digits(Cell(cOwn));
            var cur = Cell(cCurrency) is { Length: > 0 } cs ? Currency.FromStatement(cs) : Currency.RUB;
            if (txs.Count == 0) currency = cur;

            var credit = Num(cCredit) ?? 0m;
            var debit = Num(cDebit) ?? 0m;
            var amount = credit > 0 ? credit : -debit;
            if (amount == 0) { warnings.Add($"Строка {row.RowNumber()}: нет суммы"); continue; }

            var cpAcc = Digits(Cell(cCpAcc));
            var cpName = Cell(cCp);
            var purpose = Cell(cPurpose);
            var docNo = Cell(cDoc);

            string desc;
            CounterpartyRaw cp;
            if (PurposeHints.IsCardSettlementAccount(cpAcc) && PurposeHints.Merchant(purpose) is { } merchant)
            {
                var kind = PurposeHints.CardOperationKind(purpose);
                desc = kind is null ? merchant : $"{kind}: {merchant}";
                cp = new CounterpartyRaw(merchant, BankName: cpName);
            }
            else if (PurposeHints.IsBankIncomeAccount(cpAcc) || cpAcc.StartsWith("47423", StringComparison.Ordinal))
            {
                desc = purpose.Length > 0 ? (purpose.Length > 80 ? purpose[..79].TrimEnd() + "…" : purpose) : cpName;
                cp = new CounterpartyRaw(cpName.Length > 0 ? cpName : "Сбер", Account: cpAcc.Length == 20 ? cpAcc : null);
            }
            else
            {
                desc = cpName.Length > 0 ? cpName : purpose.Length > 0 ? purpose : "Операция";
                cp = new CounterpartyRaw(cpName.Length > 0 ? cpName : null, Account: cpAcc.Length == 20 ? cpAcc : null);
            }

            var baseId = StatementIds.SberDocument(date, docNo.Length > 0 ? docNo : "0", amount, cpAcc);
            var extId = baseId;
            for (var k = 2; !seenIds.Add(extId); k++) extId = $"{baseId}-{k}";

            var raw = string.Join('\t', row.CellsUsed().Select(c => c.GetString()));
            txs.Add(new ExternalTransaction(extId, ownAccount ?? "sber-business", new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3)),
                new Money(amount, cur), desc, cp, purpose.Length > 0 ? purpose : null, RawPayload: raw));
            min = min is null || date < min ? date : min;
            max = max is null || date > max ? date : max;
        }

        if (ownAccount is null) throw new InvalidDataException("В файле нет номера своего счёта (колонка «Номер счёта»)");

        // Период — из имени файла («…за_2025_09_01_2026_05_04»), иначе по датам операций
        DateRange? period = PeriodInFileName().Match(fileName) is { Success: true } m
            && DateOnly.TryParseExact($"{m.Groups["d1"]}.{m.Groups["m1"]}.{m.Groups["y1"]}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            && DateOnly.TryParseExact($"{m.Groups["d2"]}.{m.Groups["m2"]}.{m.Groups["y2"]}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
            ? new DateRange(from, to)
            : min is { } a && max is { } b ? new DateRange(a, b) : null;

        if (txs.Count == 0) warnings.Add("Операции не найдены.");
        else warnings.Add("В этом экспорте нет ИНН контрагентов и остатков по счёту. Для ИНН загрузите «Экспорт в 1С» за тот же период — операции не задвоятся, а дополнятся.");

        var account = new ExternalAccount(ownAccount, $"Сбер р/с …{ownAccount[^4..]}", AccountType.Checking, currency, ownAccount, null);
        return Task.FromResult(new StatementParseResult(account, txs, period, warnings, Institution.Codes.Sber));
    }

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    private static decimal? TryDec(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = Regex.Replace(s, @"\s", "").Replace(",", ".");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
