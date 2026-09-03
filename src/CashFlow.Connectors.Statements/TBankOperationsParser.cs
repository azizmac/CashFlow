using System.Globalization;
using System.Text;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using ClosedXML.Excel;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// Выгрузка операций Т-Банка (личный кабинет → «Выгрузить операции», XLSX/CSV).
/// Колонки: Дата операции, Дата платежа, Номер карты, Статус, Сумма операции, Валюта операции,
/// Сумма платежа, Валюта платежа, Кэшбэк, Категория, MCC, Описание, Бонусы…
/// Парсер ориентируется на заголовки, порядок колонок не важен.
/// </summary>
public sealed class TBankOperationsParser : IStatementParser
{
    public string BankCode => Institution.Codes.TBank;
    public string DisplayName => "Т-Банк: выгрузка операций";
    public IReadOnlyList<string> Extensions => [".xlsx", ".csv"];

    private static readonly string[] RequiredHeaders = ["дата операции", "сумма операции", "описание"];

    public async Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        try
        {
            var rows = await ReadRowsAsync(content, fileName, ct, maxRows: 2);
            if (rows.Count == 0) return false;
            var header = rows[0].Select(TextNormalizer.Normalize).ToHashSet();
            return RequiredHeaders.All(header.Contains);
        }
        catch { return false; }
    }

    public async Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(content, fileName, ct, maxRows: int.MaxValue);
        if (rows.Count < 2) throw new InvalidDataException("Файл пуст");

        var header = rows[0].Select(TextNormalizer.Normalize).ToList();
        int Col(string name) => header.IndexOf(name);
        int Req(string name) => Col(name) is var i && i >= 0 ? i : throw new InvalidDataException($"Нет колонки '{name}'");

        int cOpDate = Req("дата операции"), cPayDate = Col("дата платежа"), cCard = Col("номер карты"), cStatus = Col("статус"),
            cAmount = Req("сумма операции"), cCur = Col("валюта операции"), cPayAmount = Col("сумма платежа"), cPayCur = Col("валюта платежа"),
            cCategory = Col("категория"), cMcc = Col("mcc"), cDesc = Req("описание");

        var warnings = new List<string>();
        var txs = new List<ExternalTransaction>();
        string? card = null;
        Currency? accountCurrency = null;
        DateOnly? min = null, max = null;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string Cell(int i) => i >= 0 && i < row.Count ? row[i].Trim() : string.Empty;

            if (!TryParseDate(Cell(cOpDate), out var opDate)) { warnings.Add($"Строка {r + 1}: не распознана дата '{Cell(cOpDate)}'"); continue; }
            var status = Cell(cStatus);
            if (status.Equals("FAILED", StringComparison.OrdinalIgnoreCase)) continue;

            // Сумма платежа (в валюте счёта) предпочтительнее суммы операции (в валюте торговой точки)
            var amountStr = cPayAmount >= 0 && Cell(cPayAmount).Length > 0 ? Cell(cPayAmount) : Cell(cAmount);
            var curStr = cPayCur >= 0 && Cell(cPayCur).Length > 0 ? Cell(cPayCur) : Cell(cCur);
            if (!TryParseAmount(amountStr, out var amount)) { warnings.Add($"Строка {r + 1}: не распознана сумма '{amountStr}'"); continue; }
            var cur = curStr.Length > 0 ? Currency.FromStatement(curStr) : Currency.RUB;
            accountCurrency ??= cur;
            card ??= Cell(cCard).Length > 0 ? Cell(cCard) : null;

            var desc = Cell(cDesc);
            var category = Cell(cCategory);
            var mcc = Cell(cMcc) is { Length: > 0 } m && m.All(char.IsDigit) ? m.PadLeft(4, '0') : null;
            var st = status.Equals("OK", StringComparison.OrdinalIgnoreCase) || status.Length == 0 ? TransactionStatus.Posted : TransactionStatus.Pending;
            DateTimeOffset? booked = TryParseDate(Cell(cPayDate), out var pd) ? pd : null;

            var cp = ExtractCounterparty(desc, category);
            var raw = string.Join('\t', row);
            var extId = null as string; // в выгрузке нет стабильного ID операции — дедуп по содержимому

            txs.Add(new ExternalTransaction(extId, card ?? "tbank", opDate, new Money(amount, cur), desc, cp,
                Purpose: category.Length > 0 ? category : null, Mcc: mcc, Status: st, BookedAt: booked, RawPayload: raw));

            var d = DateOnly.FromDateTime(opDate.DateTime);
            min = min is null || d < min ? d : min;
            max = max is null || d > max ? d : max;
        }

        var accountName = card is { Length: > 0 } ? $"Т-Банк карта {card}" : "Т-Банк";
        var account = new ExternalAccount(card ?? "tbank", accountName, AccountType.Card, accountCurrency ?? Currency.RUB, card, null);
        var period = min is { } a && max is { } b ? new DateRange(a, b) : (DateRange?)null;
        return new StatementParseResult(account, txs, period, warnings);
    }

    private static CounterpartyRaw ExtractCounterparty(string description, string category)
    {
        // Переводы по СБП/телефону: "Перевод по номеру телефона +7 9XX..." / "Внешний перевод по номеру телефона"
        var phone = System.Text.RegularExpressions.Regex.Match(description, @"(\+7|8)[\s\-]?\(?\d{3}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}");
        if (phone.Success) return new CounterpartyRaw(description, Phone: phone.Value);
        var n = TextNormalizer.Normalize(category);
        if (n.Contains("перевод") || n.Contains("пополнен")) return new CounterpartyRaw(description);
        return new CounterpartyRaw(description);
    }

    private static bool TryParseDate(string s, out DateTimeOffset result)
    {
        var formats = new[] { "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), MoscowOffset(dt));
            return true;
        }
        result = default;
        return false;
    }

    private static TimeSpan MoscowOffset(DateTime _) => TimeSpan.FromHours(3);

    internal static bool TryParseAmount(string s, out decimal value)
    {
        var cleaned = s.Replace(" ", "").Replace(" ", "").Replace("₽", "").Replace(",", ".").Trim();
        if (cleaned.StartsWith('+')) cleaned = cleaned[1..];
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static async Task<List<List<string>>> ReadRowsAsync(Stream content, string fileName, CancellationToken ct, int maxRows)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return await ReadCsvAsync(content, ct, maxRows);

        using var wb = new XLWorkbook(content);
        var ws = wb.Worksheets.First();
        var rows = new List<List<string>>();
        foreach (var row in ws.RowsUsed())
        {
            if (rows.Count >= maxRows) break;
            var last = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            var cells = new List<string>(last);
            for (var c = 1; c <= last; c++)
            {
                var cell = row.Cell(c);
                cells.Add(cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt)
                    ? dt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                    : cell.GetFormattedString());
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static async Task<List<List<string>>> ReadCsvAsync(Stream content, CancellationToken ct, int maxRows)
    {
        // Т-Банк отдаёт CSV в Windows-1251 с разделителем ';'
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, ct);
        var data = bytes.ToArray();
        var text = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF
            ? Encoding.UTF8.GetString(data, 3, data.Length - 3)
            : LooksLikeUtf8(data) ? Encoding.UTF8.GetString(data) : Encoding.GetEncoding(1251).GetString(data);

        var rows = new List<List<string>>();
        foreach (var line in text.Split('\n'))
        {
            if (rows.Count >= maxRows) break;
            var l = line.TrimEnd('\r');
            if (l.Length == 0) continue;
            rows.Add(SplitCsv(l, ';'));
        }
        return rows;
    }

    private static bool LooksLikeUtf8(byte[] data)
    {
        try { new UTF8Encoding(false, true).GetString(data); return true; } catch { return false; }
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = !q; }
            else if (ch == sep && !q) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }
}
