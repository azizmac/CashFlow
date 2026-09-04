using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// СберБизнес / СберКазначейство: «Выписка операций по лицевому счёту». Три реальных варианта:
///  1. Краткая XLSX (СберБизнес): Дата | Вид (шифр) операции | № док. банка | № док. | БИК корр. банка | Корр. счёт | Дебет | Кредит.
///     Названий контрагентов и назначения нет — только счёт и БИК; парсер даёт описание по коду ВО и балансовому счёту.
///  2. Краткая PDF (тот же набор колонок; дебет/кредит различаем по X-координате суммы).
///  3. Расширенная XLSX (СберКазначейство): Дата проводки | Счёт Дебет | Счёт Кредит | Дебет | Кредит | № док. | ВО | Банк | Назначение —
///     в ячейках счетов три строки: номер, ИНН, название. Здесь есть всё.
/// Лучший источник для р/с всё равно формат 1С (см. <see cref="ClientBankExchangeParser"/>), но и эти читаются полностью.
/// </summary>
public sealed partial class SberBusinessStatementParser : IStatementParser
{
    public string BankCode => Institution.Codes.Sber;
    public string Code => "sber-business";
    public string DisplayName => "СберБизнес: выписка по счёту (XLSX/PDF, обычная и расширенная)";
    public IReadOnlyList<string> Extensions => [".xlsx", ".pdf"];

    [GeneratedRegex(@"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<vo>\d{2})\s+(?<docBank>\S+)\s+(?<doc>\S+)\s+(?<bik>\d{9})\s+(?<acc>\d{20})\s+(?<amt>\d[\d\s ]*[,.]\d{2})\s*$", RegexOptions.Compiled)]
    private static partial Regex PdfRow();

    [GeneratedRegex(@"СЧ[ЕЁ]Т[У]?\s+(?<acc>\d{20})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AccountRx();

    [GeneratedRegex(@"период с (?<from>\d{2} \S+ \d{4}) г\. по (?<to>\d{2} \S+ \d{4}) г\.", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PeriodWordsRx();

    [GeneratedRegex(@"период с (?<from>\d{2}\.\d{2}\.\d{4}) по (?<to>\d{2}\.\d{2}\.\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PeriodDigitsRx();

    [GeneratedRegex(@"\d[\d\s ]*[,.]\d{2}", RegexOptions.Compiled)]
    private static partial Regex MoneyRx();

    public Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        try
        {
            var text = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? PdfText(content, 1) : XlsxHeadText(content);
            var n = TextNormalizer.Normalize(text);
            return Task.FromResult(n.Contains("выписка операций по лицевому счету") && (n.Contains("сбер") || n.Contains("sber")));
        }
        catch { return Task.FromResult(false); }
    }

    public Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var parsed = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ParsePdf(content) : ParseXlsx(content);
        return Task.FromResult(Build(parsed));
    }

    private sealed record Head(string? Account, string? Currency, string? OwnerName, DateRange? Period, decimal? Opening, decimal? Closing);

    private sealed record Row(DateOnly Date, string Vo, string DocNo, string Bik, string CorrAccount, decimal Debit, decimal Credit, string Raw,
        string? CpName = null, string? CpInn = null, string? BankName = null, string? Purpose = null);

    private sealed record Parsed(Head Head, List<Row> Rows, List<string> Warnings);

    private static StatementParseResult Build(Parsed p)
    {
        var cur = p.Head.Currency is { } c && (c.Contains("доллар", StringComparison.OrdinalIgnoreCase) || c.Contains("USD")) ? Currency.USD
            : p.Head.Currency is { } c2 && (c2.Contains("евро", StringComparison.OrdinalIgnoreCase) || c2.Contains("EUR")) ? Currency.EUR : Currency.RUB;
        var accNum = p.Head.Account ?? "sber-business";
        var txs = new List<ExternalTransaction>(p.Rows.Count);
        var seenIds = new HashSet<string>();
        var extended = p.Rows.Any(r => r.CpName is not null);

        foreach (var r in p.Rows)
        {
            var amount = r.Credit > 0 ? r.Credit : -r.Debit;
            if (amount == 0) continue;

            string desc, cpName;
            string? purpose = r.Purpose;
            CounterpartyRaw cp;
            if (r.CpName is not null && PurposeHints.IsCardSettlementAccount(r.CorrAccount) && PurposeHints.Merchant(r.Purpose) is { } merchant)
            {
                // ИНН и счёт 30232/30233 принадлежат банку-эквайеру: контрагент — только название магазина
                var kind = PurposeHints.CardOperationKind(r.Purpose);
                desc = kind is null ? merchant : $"{kind}: {merchant}";
                cp = new CounterpartyRaw(merchant, BankName: r.BankName);
            }
            else
            {
                if (r.CpName is not null)
                {
                    cpName = r.CpName;
                    desc = PurposeHints.IsBankIncomeAccount(r.CorrAccount) && r.Purpose is { Length: > 0 }
                        ? (r.Purpose.Length > 80 ? r.Purpose[..79].TrimEnd() + "…" : r.Purpose)
                        : r.CpName;
                }
                else
                {
                    (desc, cpName) = Describe(r);
                    purpose ??= $"ВО {r.Vo}, док. {r.DocNo}";
                }
                cp = new CounterpartyRaw(cpName, Inn: r.CpInn, Account: r.CorrAccount, Bik: r.Bik is { Length: 9 } ? r.Bik : null, BankName: r.BankName);
            }
            // Ключ общий с ClientBankExchangeParser: один и тот же документ из XLSX, PDF и 1С дедуплицируется между форматами
            var baseId = StatementIds.SberDocument(r.Date, r.DocNo, amount, r.CorrAccount);
            var extId = baseId;
            for (var k = 2; !seenIds.Add(extId); k++) extId = $"{baseId}-{k}";

            txs.Add(new ExternalTransaction(extId, accNum, new DateTimeOffset(r.Date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3)),
                new Money(amount, cur), desc, cp, Purpose: purpose, RawPayload: r.Raw));
        }

        var name = p.Head.OwnerName is { } o ? $"Сбер р/с …{accNum[^Math.Min(4, accNum.Length)..]} ({Shorten(o)})" : $"Сбер р/с …{accNum[^Math.Min(4, accNum.Length)..]}";
        var balance = p.Head.Closing is { } cl ? new Money(cl, cur) : null;
        var account = new ExternalAccount(accNum, name, AccountType.Checking, cur, p.Head.Account, balance);
        if (txs.Count == 0) p.Warnings.Add("Операции не найдены — проверьте формат выписки.");
        if (!extended && txs.Count > 0)
            p.Warnings.Insert(0, "Краткий формат выписки: нет названий контрагентов и назначения платежа. Для полной картины выгрузите «Экспорт в 1С» (txt) или расширенную выписку.");
        return new StatementParseResult(account, txs, p.Head.Period, p.Warnings, Institution.Codes.Sber);
    }

    /// <summary>Описание по коду ВО и балансовому счёту корреспондента (краткий формат).</summary>
    private static (string Description, string CounterpartyName) Describe(Row r)
    {
        var acc = r.CorrAccount;
        var prefix5 = acc.Length >= 5 ? acc[..5] : acc;
        var prefix3 = acc.Length >= 3 ? acc[..3] : acc;
        var incoming = r.Credit > 0;

        string? cpName = prefix5 switch
        {
            "30232" or "30233" => incoming ? "Бизнес-карта (возврат/зачисление)" : "Бизнес-карта (покупка)",
            "40817" or "40820" => incoming ? "Перевод от физлица" : "Перевод физлицу",
            "40802" => incoming ? "ИП (поступление)" : "ИП (оплата)",
            "40702" or "40701" or "40703" => incoming ? "Организация (поступление)" : "Организация (оплата)",
            "40101" or "03100" => "Казначейство (налоги и взносы)",
            "47422" or "47423" => "Банк (комиссии/обязательства)",
            "70601" => "Сбер (комиссия)",
            "30301" or "30302" => "Внутрибанковский перевод",
            _ => prefix3 switch { "423" or "426" => "Вклад/депозит", "455" or "457" => "Кредит", "408" => "Клиент банка", _ => null },
        };

        var vo = r.Vo switch
        {
            "01" => "Платёжное поручение",
            "02" => "Платёжное требование",
            "06" => "Инкассовое поручение",
            "09" => "Мемориальный ордер",
            "16" => "Платёжный ордер",
            "17" => "Операция по карте",
            "11" or "12" or "13" => "Банковский ордер",
            _ => $"Операция ВО {r.Vo}",
        };

        var tail = acc.Length >= 9 ? $"{acc[..5]}…{acc[^4..]}" : acc;
        var desc = cpName is null ? $"{vo} · счёт {tail}" : $"{vo} · {cpName}";
        return (desc, cpName is null ? $"Счёт {tail} БИК {r.Bik}" : $"{cpName} · {(acc.Length >= 4 ? acc[^4..] : acc)}");
    }

    // ---------- XLSX ----------

    private static string XlsxHeadText(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var sb = new StringBuilder();
        foreach (var row in ws.Rows(1, Math.Min(20, ws.LastRowUsed()?.RowNumber() ?? 1)))
            foreach (var c in row.CellsUsed()) sb.Append(CellText(c)).Append(' ');
        return sb.ToString();
    }

    private static string CellText(IXLCell c) =>
        c.Value.IsNumber ? c.Value.GetNumber().ToString(CultureInfo.InvariantCulture)
        : c.Value.IsDateTime ? c.Value.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : c.GetFormattedString().Trim();

    private static Parsed ParseXlsx(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var warnings = new List<string>();
        string? account = null, currency = null, owner = null;
        DateRange? period = null;
        decimal? opening = null, closing = null;
        var rows = new List<Row>();
        int cDate = -1, cVo = -1, cDoc = -1, cBik = -1, cAcc = -1, cDebit = -1, cCredit = -1, cAccDebit = -1, cAccCredit = -1, cBank = -1, cPurpose = -1;
        var extended = false;
        var prevWasTitle = false;

        foreach (var row in ws.RowsUsed())
        {
            var cells = row.CellsUsed().ToDictionary(c => c.Address.ColumnNumber, CellText);
            if (cells.Count == 0) continue;
            var joined = string.Join(" | ", cells.Values);
            var norm = TextNormalizer.Normalize(joined);

            if (cDate < 0)
            {
                // Шапка
                if (norm.Contains("выписка операций по лицевому счету"))
                {
                    account ??= AccountRx().Match(joined) is { Success: true } am ? am.Groups["acc"].Value : null;
                    prevWasTitle = true;
                    continue;
                }
                if (prevWasTitle)
                {
                    prevWasTitle = false;
                    if (!norm.StartsWith("за период") && !norm.StartsWith("счет") && joined.Length > 3) owner ??= joined;
                }
                if (norm.StartsWith("счет") || norm.Contains("| счет") || norm.Contains("счет |"))
                {
                    account ??= cells.Values.FirstOrDefault(v => Regex.IsMatch(v, @"^\d{20}$"));
                }
                if (norm.Contains("рубл") || norm.Contains("доллар") || norm.Contains("евро"))
                    currency ??= cells.Values.FirstOrDefault(v => TextNormalizer.Normalize(v) is var nv && (nv.Contains("рубл") || nv.Contains("доллар") || nv.Contains("евро")));
                if (norm.StartsWith("название")) owner ??= cells.Values.Skip(1).FirstOrDefault(v => v.Length > 3 && !v.StartsWith("Отв"));
                if (PeriodWordsRx().Match(joined) is { Success: true } pm) period = ParsePeriod(pm);
                else if (PeriodDigitsRx().Match(joined) is { Success: true } pd) period = new DateRange(DateOnly.ParseExact(pd.Groups["from"].Value, "dd.MM.yyyy"), DateOnly.ParseExact(pd.Groups["to"].Value, "dd.MM.yyyy"));
                if (norm.StartsWith("входящий остаток")) opening = LastMoney(cells.Values);

                if (norm.Contains("дата") && (norm.Contains("дебету") || norm.Contains("дебет")))
                {
                    extended = norm.Contains("назначение");
                    foreach (var (col, v) in cells)
                    {
                        var n = TextNormalizer.Normalize(v);
                        if (n.StartsWith("дата")) cDate = col;
                        else if (n.StartsWith("вид") || n == "во") cVo = col;
                        else if (n.StartsWith("номер документа банка")) { /* не используем */ }
                        else if (n.StartsWith("номер документа") || n.StartsWith("№ документа")) cDoc = col;
                        else if (n.StartsWith("бик")) cBik = col;
                        else if (n.StartsWith("корреспондирующий")) cAcc = col;
                        else if (n == "счет") { cAccDebit = col; cAccCredit = col + 1; }
                        else if (n.Contains("дебету")) cDebit = col;
                        else if (n.Contains("кредиту")) cCredit = col;
                        else if (n.StartsWith("банк")) cBank = col;
                        else if (n.StartsWith("назначение")) cPurpose = col;
                    }
                    if (cDoc < 0 && cVo >= 0 && !extended) cDoc = cVo + 1;
                    if (extended && account is null) warnings.Add("Не найден номер своего счёта в заголовке расширенной выписки.");
                }
                continue;
            }

            // Подзаголовок расширенной выписки («Дебет | Кредит») и служебные строки
            if (norm == "дебет | кредит" || norm.StartsWith("дебет |")) continue;
            if (norm.StartsWith("исходящий остаток")) { closing = LastMoney(cells.Values); continue; }
            if (norm.StartsWith("входящий остаток")) { opening ??= LastMoney(cells.Values); continue; }
            if (norm.StartsWith("всего") || norm.StartsWith("итого") || norm.StartsWith("количество операций") || norm.StartsWith("б/с")) continue;

            var dateStr = cells.GetValueOrDefault(cDate) ?? "";
            if (!DateOnly.TryParseExact(dateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            if (extended)
            {
                var debitSide = SplitParty(cells.GetValueOrDefault(cAccDebit));
                var creditSide = SplitParty(cells.GetValueOrDefault(cAccCredit));
                var own = account;
                var isDebit = own is not null ? debitSide.Account == own : creditSide.Account is not null && TryDec(cells.GetValueOrDefault(cDebit)) is > 0;
                var cp = isDebit ? creditSide : debitSide;
                var (bik, bankName) = SplitBank(cells.GetValueOrDefault(cBank));
                var debit = isDebit ? TryDec(cells.GetValueOrDefault(cDebit)) ?? 0m : 0m;
                var credit = isDebit ? 0m : TryDec(cells.GetValueOrDefault(cCredit)) ?? 0m;
                rows.Add(new Row(date, cells.GetValueOrDefault(cVo) ?? "", cells.GetValueOrDefault(cDoc) ?? "", bik ?? "", cp.Account ?? "",
                    debit, credit, joined, cp.Name ?? bankName ?? "Контрагент", cp.Inn, bankName, cells.GetValueOrDefault(cPurpose)));
            }
            else
            {
                var acc = cells.GetValueOrDefault(cAcc) ?? "";
                if (acc.Length != 20) warnings.Add($"Строка {row.RowNumber()}: нет корр. счёта");
                rows.Add(new Row(date, cells.GetValueOrDefault(cVo) ?? "", cells.GetValueOrDefault(cDoc) ?? "", cells.GetValueOrDefault(cBik) ?? "", acc,
                    TryDec(cells.GetValueOrDefault(cDebit)) ?? 0m, TryDec(cells.GetValueOrDefault(cCredit)) ?? 0m, joined));
            }
        }

        if (cDate < 0) warnings.Add("Не найдена шапка таблицы операций.");
        return new Parsed(new Head(account, currency, owner, period, opening, closing), rows, warnings);
    }

    /// <summary>Ячейка расширенной выписки: «40702810…⏎7700000002⏎ООО "Ромашка"».</summary>
    private static (string? Account, string? Inn, string? Name) SplitParty(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return (null, null, null);
        var parts = cell.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        string? acc = null, inn = null;
        var rest = new List<string>();
        foreach (var p in parts)
        {
            // У физлиц без ИНН номер счёта бывает склеен с именем в одной строке: «40817810…0004Петрова Мария»
            var m = Regex.Match(p, @"^(?<acc>\d{20})(?<tail>.*)$");
            if (acc is null && m.Success)
            {
                acc = m.Groups["acc"].Value;
                if (m.Groups["tail"].Value.Trim() is { Length: > 0 } tail) rest.Add(tail);
                continue;
            }
            if (inn is null && Regex.IsMatch(p, @"^\d{10}$|^\d{12}$")) { inn = p; continue; }
            rest.Add(p);
        }
        var name = string.Join(' ', rest);
        return (acc, inn, name.Length > 0 ? name : null);
    }

    private static (string? Bik, string? Name) SplitBank(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return (null, null);
        var m = Regex.Match(cell, @"БИК\s*(?<bik>\d{9})", RegexOptions.IgnoreCase);
        var name = Regex.Replace(cell, @"БИК\s*\d{9}", "", RegexOptions.IgnoreCase).Replace('\n', ' ').Trim();
        return (m.Success ? m.Groups["bik"].Value : null, name.Length > 0 ? name : null);
    }

    /// <summary>Последняя сумма в строке итогов («Исходящий остаток | 0,00 | 95 439,28 (П) | 31.07.2026»); даты пропускаются.</summary>
    private static decimal? LastMoney(IEnumerable<string> cells)
    {
        decimal? last = null;
        foreach (var v in cells)
        {
            var t = v.Trim();
            if (Regex.IsMatch(t, @"^\d{2}\.\d{2}\.\d{4}$")) continue;
            var m = MoneyRx().Match(t);
            if (m.Success && TryDec(m.Value) is { } d) last = d;
            else if (TryDec(t) is { } d2) last = d2;
        }
        return last;
    }

    // ---------- PDF ----------

    private sealed record PdfLine(string Text, double LastWordRight, double PageWidth);

    private static List<PdfLine> PdfLines(Stream s, int maxPages)
    {
        using var doc = PdfDocument.Open(s);
        var result = new List<PdfLine>();
        var n = 0;
        foreach (var page in doc.GetPages())
        {
            if (n++ >= maxPages) break;
            var lines = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.5))
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList());
            foreach (var ws in lines)
                result.Add(new PdfLine(string.Join(' ', ws.Select(w => w.Text)), ws[^1].BoundingBox.Right, page.Width));
        }
        return result;
    }

    private static string PdfText(Stream s, int maxPages) => string.Join('\n', PdfLines(s, maxPages).Select(l => l.Text));

    private static Parsed ParsePdf(Stream s)
    {
        var lines = PdfLines(s, int.MaxValue);
        var text = string.Join('\n', lines.Select(l => l.Text));
        var warnings = new List<string>();
        var account = AccountRx().Match(text) is { Success: true } am ? am.Groups["acc"].Value : null;
        DateRange? period = PeriodWordsRx().Match(text) is { Success: true } pm ? ParsePeriod(pm)
            : PeriodDigitsRx().Match(text) is { Success: true } pd ? new DateRange(DateOnly.ParseExact(pd.Groups["from"].Value, "dd.MM.yyyy"), DateOnly.ParseExact(pd.Groups["to"].Value, "dd.MM.yyyy")) : null;
        var owner = Regex.Match(text, @"НАЗВАНИЕ\s+(?<n>.+?)\s+Отв\.", RegexOptions.IgnoreCase) is { Success: true } om ? om.Groups["n"].Value.Trim() : null;
        decimal? opening = Regex.Match(text, @"Входящий остаток.*?(?<a>\d[\d\s ]*,\d{2})") is { Success: true } o ? TryDec(o.Groups["a"].Value) : null;
        decimal? closing = Regex.Match(text, @"Исходящий остаток.*?(?<a>\d[\d\s ]*,\d{2})") is { Success: true } c ? TryDec(c.Groups["a"].Value) : null;
        var currency = text.Contains("Российский рубль") ? "Российский рубль" : text.Contains("Доллар") ? "Доллар США" : null;

        // Дебет и кредит — две последние колонки; различаем по правому краю суммы относительно ширины страницы:
        // дебет заканчивается ≈0.83 ширины, кредит ≈0.96 (калибровано по реальной выписке).
        var rows = new List<Row>();
        foreach (var l in lines)
        {
            var m = PdfRow().Match(l.Text.Trim());
            if (!m.Success) continue;
            var date = DateOnly.ParseExact(m.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var amt = TryDec(m.Groups["amt"].Value) ?? 0m;
            var isCredit = l.LastWordRight / l.PageWidth >= 0.88;
            rows.Add(new Row(date, m.Groups["vo"].Value, m.Groups["doc"].Value, m.Groups["bik"].Value, m.Groups["acc"].Value, isCredit ? 0 : amt, isCredit ? amt : 0, l.Text.Trim()));
        }
        return new Parsed(new Head(account, currency, owner, period, opening, closing), rows, warnings);
    }

    private static DateRange ParsePeriod(Match m)
    {
        var ru = new CultureInfo("ru-RU");
        DateOnly P(string v) => DateOnly.TryParseExact(v, "dd MMMM yyyy", ru, DateTimeStyles.None, out var d) ? d : DateOnly.ParseExact(v, "dd MMMM yyyy", CultureInfo.InvariantCulture);
        return new DateRange(P(m.Groups["from"].Value), P(m.Groups["to"].Value));
    }

    private static decimal? TryDec(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // В XLSX Сбера разряды разделены узким пробелом (U+202F) или NBSP — убираем любые пробельные символы
        var cleaned = Regex.Replace(Regex.Replace(s, @"\s*\([ПАпа]\)\s*$", ""), @"\s", "").Replace(",", ".");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string Shorten(string owner)
    {
        var n = TextNormalizer.Normalize(owner);
        if (n.StartsWith("индивидуальный предприниматель ")) return "ИП " + owner[31..].Trim();
        if (n.StartsWith("общество с ограниченной ответственностью ")) return "ООО " + owner[40..].Trim();
        return owner.Length > 40 ? owner[..40] + "…" : owner;
    }
}
