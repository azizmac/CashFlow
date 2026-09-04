// Диагностическая утилита: прогоняет парсеры выписок по файлам и печатает, что распозналось.
// Использование: dotnet run --project tools/StatementProbe -- <файл> [<файл> ...] [--raw]
//   --raw  — дополнительно вывести сырые строки PDF (PdfPig) / первые строки XLSX.
using System.Globalization;
using System.Text;
using CashFlow.Connectors.Abstractions;
using CashFlow.Connectors.Statements;
using ClosedXML.Excel;
using UglyToad.PdfPig;

Console.OutputEncoding = Encoding.UTF8;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var raw = args.Contains("--raw");
var dbIdx = Array.IndexOf(args, "--db");
var files = args.Where((a, i) => !a.StartsWith("--") && (dbIdx < 0 || i != dbIdx + 1)).ToList();
if (dbIdx >= 0)
{
    // Сквозной прогон через реальную БД: миграции → сид → импорт файлов дважды → сводка
    await ImportProbe.RunAsync(args[dbIdx + 1], files);
    return 0;
}
if (files.Count == 0) { Console.WriteLine("usage: StatementProbe <file> [...] [--raw]"); return 1; }

IStatementParser[] parsers = [new TBankOperationsParser(), new SberPdfStatementParser(), new SberBusinessStatementParser(), new SberBusinessOperationsParser(), new ClientBankExchangeParser()];

foreach (var path in files)
{
    Console.WriteLine($"\n==================== {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
    var bytes = await File.ReadAllBytesAsync(path);

    if (raw && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) DumpPdf(bytes, 70);
    if (raw && path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) DumpXlsx(bytes, 40);

    foreach (var p in parsers)
    {
        if (!p.Extensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
        bool can;
        try { using var ms = new MemoryStream(bytes); can = await p.CanParseAsync(ms, path, default); }
        catch (Exception ex) { Console.WriteLine($"  [{p.DisplayName}] CanParse threw: {ex.Message}"); continue; }
        Console.WriteLine($"  [{p.DisplayName}] CanParse = {can}");
        if (!can) continue;
        try
        {
            using var ms = new MemoryStream(bytes);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await p.ParseAsync(ms, path, default);
            Console.WriteLine($"    account: {r.Account.Name} | ext={r.Account.ExternalId} | num={r.Account.AccountNumber} | {r.Account.Type} {r.Account.Currency} | balance={r.Account.Balance?.ToString() ?? "-"}");
            Console.WriteLine($"    period: {r.Period?.From:yyyy-MM-dd}..{r.Period?.To:yyyy-MM-dd} | transactions: {r.Transactions.Count} | {sw.ElapsedMilliseconds} ms");
            var inc = r.Transactions.Where(t => t.Amount.Amount > 0).Sum(t => t.Amount.Amount);
            var exp = r.Transactions.Where(t => t.Amount.Amount < 0).Sum(t => t.Amount.Amount);
            Console.WriteLine($"    sum income: {inc:N2} | sum expense: {exp:N2} | with counterparty name: {r.Transactions.Count(t => t.Counterparty.Name is { Length: > 0 })} | with INN: {r.Transactions.Count(t => t.Counterparty.Inn is { Length: > 0 })} | with extId: {r.Transactions.Count(t => t.ExternalId is { Length: > 0 })}");
            foreach (var w in r.Warnings.Take(8)) Console.WriteLine($"    ! {w}");
            if (r.Warnings.Count > 8) Console.WriteLine($"    ! ... ещё {r.Warnings.Count - 8}");
            foreach (var t in r.Transactions.Take(12))
                Console.WriteLine($"    {t.PostedAt:yyyy-MM-dd HH:mm} {t.Amount.Amount,14:N2} {t.Status,-7} | {Trunc(t.Description, 48),-48} | cp: {Trunc(t.Counterparty.Name, 30)} inn={t.Counterparty.Inn} acc={t.Counterparty.Account} | purpose: {Trunc(t.Purpose, 40)} | mcc={t.Mcc} ext={t.ExternalId}");
            if (r.Transactions.Count > 12)
            {
                Console.WriteLine("    ...");
                foreach (var t in r.Transactions.TakeLast(3))
                    Console.WriteLine($"    {t.PostedAt:yyyy-MM-dd HH:mm} {t.Amount.Amount,14:N2} {t.Status,-7} | {t.Description} | cp: {t.Counterparty.Name} inn={t.Counterparty.Inn} acc={t.Counterparty.Account} | purpose: {Trunc(t.Purpose, 40)} | raw: {t.RawPayload?.Replace('\n', '⏎')}");
            }
            var dupes = r.Transactions.Where(t => t.ExternalId is not null).GroupBy(t => t.ExternalId).Count(g => g.Count() > 1);
            if (dupes > 0) Console.WriteLine($"    !! duplicate external ids: {dupes}");
        }
        catch (Exception ex) { Console.WriteLine($"    Parse threw: {ex}"); }
    }
}
return 0;

static string Trunc(string? s, int n) => s is null ? "" : s.Length <= n ? s : s[..(n - 1)] + "…";

static void DumpPdf(byte[] bytes, int maxLines)
{
    using var doc = PdfDocument.Open(bytes);
    Console.WriteLine($"  -- PDF: {doc.NumberOfPages} pages; first lines grouped by Y (PdfPig):");
    var n = 0;
    foreach (var page in doc.GetPages())
    {
        var lines = page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.5))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList());
        foreach (var ws in lines)
        {
            if (n++ >= maxLines) return;
            Console.WriteLine($"  {ws[0].BoundingBox.Left,6:0} {ws[^1].BoundingBox.Right / page.Width,5:0.00} | {string.Join(' ', ws.Select(w => w.Text))}");
        }
        if (n >= maxLines) return;
    }
}

static void DumpXlsx(byte[] bytes, int maxRows)
{
    using var wb = new XLWorkbook(new MemoryStream(bytes));
    foreach (var ws in wb.Worksheets)
    {
        Console.WriteLine($"  -- XLSX sheet '{ws.Name}': rows={ws.LastRowUsed()?.RowNumber()} cols={ws.LastColumnUsed()?.ColumnNumber()}");
        var all = ws.RowsUsed().ToList();
        foreach (var row in all.Take(maxRows).Concat(all.Count > maxRows * 2 ? all.TakeLast(maxRows) : all.Skip(maxRows)))
        {
            var cells = row.CellsUsed().Select(c => $"{c.Address.ColumnLetter}={Trunc((c.Value.IsNumber ? c.Value.GetNumber().ToString(CultureInfo.InvariantCulture) : c.GetFormattedString()).Replace("\n", "⏎").Replace("\r", ""), 90)}");
            Console.WriteLine($"  r{row.RowNumber(),-3} {string.Join(" | ", cells)}");
        }
    }
}
