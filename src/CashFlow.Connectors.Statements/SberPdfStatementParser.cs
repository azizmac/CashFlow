using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;
using UglyToad.PdfPig;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// PDF «Выписка по счёту дебетовой карты» физлица из СберБанк Онлайн (проверено на реальном файле, 20 страниц).
/// Каждая операция — две строки таблицы:
///   «ДД.ММ.ГГГГ ЧЧ:ММ  КАТЕГОРИЯ  СУММА  ОСТАТОК»
///   «ДД.ММ.ГГГГ КОД_АВТОРИЗАЦИИ  Описание операции. Операция по карте ****1234» (описание может переноситься на 1–2 строки).
/// Расходы без знака, поступления со знаком «+». Код авторизации используется как внешний ID.
/// </summary>
public sealed partial class SberPdfStatementParser : IStatementParser
{
    public string BankCode => Institution.Codes.Sber;
    public string Code => "sber-card-pdf";
    public string DisplayName => "Сбер: PDF-выписка по счёту карты (физлицо)";
    public IReadOnlyList<string> Extensions => [".pdf"];

    [GeneratedRegex(@"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<time>\d{2}:\d{2})\s+(?<category>.+?)\s+(?<amount>[+\-−]?\s?\d[\d\s ]*[,.]\d{2})\s+(?<balance>[+\-−]?\s?\d[\d\s ]*[,.]\d{2})\s*$", RegexOptions.Compiled)]
    private static partial Regex OperationLine();

    [GeneratedRegex(@"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?:(?<code>\d{6})\s+)?(?<desc>.+)$", RegexOptions.Compiled)]
    private static partial Regex DescriptionLine();

    [GeneratedRegex(@"Номер сч[её]та\s+(?<acc>\d[\d ]{18,30}\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AccountNumber();

    [GeneratedRegex(@"Карта\s+(?<name>[^\n]*?)\s*(?:•+|\*+)\s*(?<last4>\d{4})", RegexOptions.Compiled)]
    private static partial Regex CardLine();

    [GeneratedRegex(@"Остаток на (?<date>\d{2}\.\d{2}\.\d{4})\s+(?<amt>[+\-−]?\s?\d[\d\s ]*[,.]\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BalanceLine();

    [GeneratedRegex(@"За период\s+(?<from>\d{2}\.\d{2}\.\d{4})\s*[-–—]\s*(?<to>\d{2}\.\d{2}\.\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex Period();

    [GeneratedRegex(@"\.?\s*Операция\s+по\s*(?:карте)?\s*(?:•+|\*+)?\s*\d{0,4}\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CardSuffix();

    [GeneratedRegex(@"^(?:Продолжение|Действителен|Для проверки|Выписка по|Страница|Дата формирования|Итого|\d\.\s|до \d|с \d{2}\.\d{2}\.\d{4} по|\*(?:\s|$)|[0-9A-F]{32}$|\d{2}\.\d{2}\.\d{4}$)", RegexOptions.Compiled)]
    private static partial Regex FooterLine();

    public Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        try
        {
            var n = TextNormalizer.Normalize(ExtractText(content, maxPages: 1));
            var isCard = n.Contains("выписка по счету дебетовой карты") || n.Contains("выписка по счету кредитной карты") || (n.Contains("выписка по счету") && n.Contains("карта"));
            return Task.FromResult(isCard && !n.Contains("лицевому счету") && (n.Contains("сбер") || n.Contains("sber")));
        }
        catch { return Task.FromResult(false); }
    }

    public Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var text = ExtractText(content, maxPages: int.MaxValue);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var warnings = new List<string>();

        string? accNum = null;
        if (AccountNumber().Match(text) is { Success: true } am)
        {
            var digits = Regex.Replace(am.Groups["acc"].Value, @"\s", "");
            if (digits.Length == 20) accNum = digits;
        }
        string? cardName = null, last4 = null;
        if (CardLine().Match(text) is { Success: true } cm) { cardName = cm.Groups["name"].Value.Trim(); last4 = cm.Groups["last4"].Value; }
        var currency = text.Contains("Российский рубль") || text.Contains("RUB") || text.Contains("₽") ? Currency.RUB
            : text.Contains("Доллар", StringComparison.OrdinalIgnoreCase) || text.Contains("USD") ? Currency.USD
            : text.Contains("Евро", StringComparison.OrdinalIgnoreCase) || text.Contains("EUR") ? Currency.EUR : Currency.RUB;

        DateRange? period = Period().Match(text) is { Success: true } pm
            ? new DateRange(DateOnly.ParseExact(pm.Groups["from"].Value, "dd.MM.yyyy"), DateOnly.ParseExact(pm.Groups["to"].Value, "dd.MM.yyyy"))
            : null;

        var accountKey = accNum ?? (last4 is not null ? "****" + last4 : "sber-card");
        var txs = new List<ExternalTransaction>();
        var seenIds = new HashSet<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var m = OperationLine().Match(lines[i]);
            if (!m.Success) continue;

            var date = DateTime.ParseExact(m.Groups["date"].Value + " " + m.Groups["time"].Value, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            var category = m.Groups["category"].Value.Trim();
            var amountRaw = m.Groups["amount"].Value;
            if (!TryAmount(amountRaw, out var amount)) { warnings.Add($"Не распознана сумма: '{lines[i]}'"); continue; }
            var signed = amountRaw.TrimStart().StartsWith('+') ? Math.Abs(amount) : -Math.Abs(amount);

            string desc = category;
            string? code = null;
            DateTimeOffset? booked = null;
            var rawLines = new List<string> { lines[i] };

            if (i + 1 < lines.Count && !OperationLine().IsMatch(lines[i + 1]) && DescriptionLine().Match(lines[i + 1]) is { Success: true } dm)
            {
                desc = dm.Groups["desc"].Value.Trim();
                code = dm.Groups["code"].Success ? dm.Groups["code"].Value : null;
                booked = new DateTimeOffset(DateTime.ParseExact(dm.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture), TimeSpan.FromHours(3));
                i++;
                rawLines.Add(lines[i]);
                // Описание может переноситься на следующие строки («по карте ****1234», «карте ****1234»), но не больше двух
                var extra = 0;
                while (i + 1 < lines.Count && extra++ < 2 && IsContinuation(lines[i + 1]))
                {
                    desc += " " + lines[i + 1];
                    i++;
                    rawLines.Add(lines[i]);
                }
            }

            desc = CleanDescription(desc);
            var cp = ExtractCounterparty(desc, category, out var mcc);
            string? extId = null;
            if (code is not null)
            {
                extId = $"{date:yyyyMMdd}-{code}";
                if (!seenIds.Add(extId)) { var k = 2; while (!seenIds.Add($"{extId}-{k}")) k++; extId = $"{extId}-{k}"; }
            }

            txs.Add(new ExternalTransaction(extId, accountKey, new DateTimeOffset(date, TimeSpan.FromHours(3)), new Money(signed, currency), desc, cp,
                Purpose: category, Mcc: mcc, Status: TransactionStatus.Posted, BookedAt: booked, RawPayload: string.Join('\n', rawLines)));
        }

        if (txs.Count == 0) warnings.Add("Не найдено ни одной операции — возможно, формат выписки отличается.");

        // Остатков в шапке два: на начало и на конец периода. Берём на конец (или последний найденный).
        Money? balance = null;
        var balances = BalanceLine().Matches(text).Select(b => (Date: b.Groups["date"].Value, Amt: b.Groups["amt"].Value)).ToList();
        var closing = period is { } p ? balances.FirstOrDefault(b => b.Date == p.To.ToString("dd.MM.yyyy")) : default;
        if (closing == default && balances.Count > 0) closing = balances[^1];
        if (closing != default && TryAmount(closing.Amt, out var bal)) balance = new Money(bal, currency);

        period ??= txs.Count > 0 ? new DateRange(txs.Min(t => DateOnly.FromDateTime(t.PostedAt.DateTime)), txs.Max(t => DateOnly.FromDateTime(t.PostedAt.DateTime))) : null;

        var name = last4 is not null
            ? $"Сбер {(string.IsNullOrWhiteSpace(cardName) ? "карта" : cardName)} ****{last4}"
            : accNum is not null ? $"Сбер счёт …{accNum[^4..]}" : "Сбер карта";
        var account = new ExternalAccount(accountKey, name, AccountType.Card, currency, accNum ?? (last4 is not null ? "****" + last4 : null), balance);
        return Task.FromResult(new StatementParseResult(account, txs, period, warnings, Institution.Codes.Sber));
    }

    private static bool IsContinuation(string next)
    {
        if (OperationLine().IsMatch(next) || DescriptionLine().IsMatch(next)) return false;
        if (next.Length > 90 || next.Contains("ОСТАТОК") || FooterLine().IsMatch(next)) return false;
        return true;
    }

    /// <summary>Убирает хвост «. Операция по карте ****1234» и обрывки переносов.</summary>
    internal static string CleanDescription(string desc)
    {
        var d = Regex.Replace(desc, @"\s+", " ").Trim();
        d = CardSuffix().Replace(d, "");
        d = Regex.Replace(d, @"\s*(?:•+|\*{2,})\s*\d{4}\s*$", "");
        d = Regex.Replace(d, @"\s+(?:по\s+)?карте\s*$", "", RegexOptions.IgnoreCase);
        return d.Trim().TrimEnd('.').Trim();
    }

    internal static CounterpartyRaw ExtractCounterparty(string desc, string category, out string? mcc)
    {
        mcc = null;
        var phone = Regex.Match(desc, @"(\+7|8)[\s\-]?\(?\d{3}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}");
        if (phone.Success) return new CounterpartyRaw(desc, Phone: phone.Value);

        // «Перевод для П. Иван Сергеевич», «Перевод от С. Пётр», «Перевод из T-Bank», «Перевод в Yandex»
        var transfer = Regex.Match(desc, @"^Перевод\s+(?:для|от|из|в|на|с)\s+(?<name>.+?)\s*$", RegexOptions.IgnoreCase);
        if (transfer.Success) return new CounterpartyRaw(transfer.Groups["name"].Value.Trim().TrimEnd('.'));

        // Торговая точка с MCC в описании: «SBER*5411*MARKET MOSCOW RUS»
        var merchant = Regex.Match(desc, @"^SBER\*(?<mcc>\d{4})\*(?<name>.+)$", RegexOptions.IgnoreCase);
        if (merchant.Success) { mcc = merchant.Groups["mcc"].Value; return new CounterpartyRaw(merchant.Groups["name"].Value.Trim()); }

        if (Regex.IsMatch(desc, @"^Банкомат", RegexOptions.IgnoreCase)) return new CounterpartyRaw("Банкомат Сбер");
        if (desc.Contains("VKLAD-KARTA", StringComparison.OrdinalIgnoreCase) || desc.Contains("KARTA-VKLAD", StringComparison.OrdinalIgnoreCase)) return new CounterpartyRaw("Сбер вклад");
        if (Regex.IsMatch(desc, @"^Прочие выплаты$", RegexOptions.IgnoreCase)) return new CounterpartyRaw("Сбер: прочие выплаты");

        // «G425 Магазин» (оплата по QR СБП) — убираем код точки
        var qr = Regex.Match(desc, @"^[A-Z0-9]{3,6}\s+(?<name>[А-ЯЁа-яё].+)$");
        if (qr.Success && category.Contains("QR", StringComparison.OrdinalIgnoreCase)) return new CounterpartyRaw(qr.Groups["name"].Value.Trim());

        return new CounterpartyRaw(desc);
    }

    internal static bool TryAmount(string s, out decimal v)
    {
        var cleaned = Regex.Replace(s, @"\s", "").Replace("−", "-").Replace(",", ".").TrimStart('+');
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
    }

    private static string ExtractText(Stream content, int maxPages)
    {
        using var doc = PdfDocument.Open(content);
        var sb = new StringBuilder();
        var n = 0;
        foreach (var page in doc.GetPages())
        {
            if (n++ >= maxPages) break;
            // Группируем слова по строкам по координате Y, чтобы получить табличные строки
            var words = page.GetWords().ToList();
            var lines = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(' ', g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
            foreach (var l in lines) sb.AppendLine(l);
        }
        return sb.ToString();
    }
}
