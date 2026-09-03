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
/// PDF-выписка Сбера по счёту карты физлица («Выписка по счёту дебетовой карты» из СберБанк Онлайн).
/// Формат таблицы: строка 1 — «ДД.ММ.ГГГГ ЧЧ:ММ  КОД  КАТЕГОРИЯ  СУММА  ОСТАТОК», строка 2 — «ДД.ММ.ГГГГ  ОПИСАНИЕ».
/// Парсер работает по тексту, поэтому устойчив к небольшим изменениям вёрстки, но не к смене структуры.
/// </summary>
public sealed partial class SberPdfStatementParser : IStatementParser
{
    public string BankCode => Institution.Codes.Sber;
    public string DisplayName => "Сбер: PDF-выписка по карте";
    public IReadOnlyList<string> Extensions => [".pdf"];

    [GeneratedRegex(@"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<time>\d{2}:\d{2})\s+(?<code>\d{4,8})?\s*(?<category>.+?)\s+(?<amount>[+\-−]?\s?\d[\d\s ]*[,.]\d{2})\s+(?<balance>\d[\d\s ]*[,.]\d{2})\s*$", RegexOptions.Compiled)]
    private static partial Regex OperationLine();

    [GeneratedRegex(@"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<desc>.+)$", RegexOptions.Compiled)]
    private static partial Regex DescriptionLine();

    [GeneratedRegex(@"(?:Номер счёта|Номер счета|Счёт|Счет)[:\s]+(?<acc>\d{20})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AccountNumber();

    [GeneratedRegex(@"(?:Номер карты|Карта)[:\s]+(?<card>(?:\*{4}|\d{4})[\s\d\*]{4,})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CardNumber();

    [GeneratedRegex(@"ОСТАТОК НА \d{2}\.\d{2}\.\d{4}[:\s]+(?<amt>\d[\d\s ]*[,.]\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ClosingBalance();

    [GeneratedRegex(@"Период[:\s]+(?<from>\d{2}\.\d{2}\.\d{4})\s*[-–—]\s*(?<to>\d{2}\.\d{2}\.\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex Period();

    public Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        try
        {
            var text = ExtractText(content, maxPages: 1);
            var n = text.ToLowerInvariant();
            return Task.FromResult((n.Contains("сбер") || n.Contains("sber")) && (n.Contains("выписка") || n.Contains("остаток")));
        }
        catch { return Task.FromResult(false); }
    }

    public Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var text = ExtractText(content, maxPages: int.MaxValue);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var warnings = new List<string>();

        var accNum = AccountNumber().Match(text) is { Success: true } am ? am.Groups["acc"].Value : null;
        var card = CardNumber().Match(text) is { Success: true } cm ? Regex.Replace(cm.Groups["card"].Value, @"\s", "") : null;
        var currency = text.Contains("USD") && !text.Contains("RUB") && !text.Contains("₽") ? Currency.USD : Currency.RUB;

        var txs = new List<ExternalTransaction>();
        var accountKey = accNum ?? card ?? "sber";

        for (var i = 0; i < lines.Count; i++)
        {
            var m = OperationLine().Match(lines[i]);
            if (!m.Success) continue;

            var date = DateTime.ParseExact(m.Groups["date"].Value + " " + m.Groups["time"].Value, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            var category = m.Groups["category"].Value.Trim();
            var amountRaw = m.Groups["amount"].Value;
            if (!TryAmount(amountRaw, out var amount)) { warnings.Add($"Не распознана сумма: '{lines[i]}'"); continue; }

            // В выписке Сбера расходы — без знака, доходы — с плюсом.
            var signed = amountRaw.TrimStart().StartsWith('+') ? Math.Abs(amount) : -Math.Abs(amount);

            string desc = category;
            DateTimeOffset? booked = null;
            if (i + 1 < lines.Count && DescriptionLine().Match(lines[i + 1]) is { Success: true } dm && !OperationLine().IsMatch(lines[i + 1]))
            {
                desc = dm.Groups["desc"].Value.Trim();
                booked = new DateTimeOffset(DateTime.ParseExact(dm.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture), TimeSpan.FromHours(3));
                i++;
                // Описание может продолжаться на следующей строке без даты
                while (i + 1 < lines.Count && !OperationLine().IsMatch(lines[i + 1]) && !DescriptionLine().IsMatch(lines[i + 1]) && !lines[i + 1].StartsWith("Продолжение") && lines[i + 1].Length < 120 && !lines[i + 1].Contains("ОСТАТОК"))
                {
                    desc += " " + lines[i + 1];
                    i++;
                }
            }

            var cp = ExtractCounterparty(desc, category);
            var code = m.Groups["code"].Success ? m.Groups["code"].Value : null;
            var extId = code is not null ? $"{date:yyyyMMddHHmm}-{code}-{Math.Abs(signed):0.00}" : null;

            txs.Add(new ExternalTransaction(extId, accountKey, new DateTimeOffset(date, TimeSpan.FromHours(3)), new Money(signed, currency), desc, cp,
                Purpose: category, Status: TransactionStatus.Posted, BookedAt: booked, RawPayload: lines[i - (booked is null ? 0 : 1)] + "\n" + (booked is null ? "" : lines[i])));
        }

        if (txs.Count == 0) warnings.Add("Не найдено ни одной операции — возможно, формат выписки отличается.");

        Money? balance = ClosingBalance().Match(text) is { Success: true } bm && TryAmount(bm.Groups["amt"].Value, out var bal) ? new Money(bal, currency) : null;
        DateRange? period = Period().Match(text) is { Success: true } pm
            ? new DateRange(DateOnly.ParseExact(pm.Groups["from"].Value, "dd.MM.yyyy"), DateOnly.ParseExact(pm.Groups["to"].Value, "dd.MM.yyyy"))
            : txs.Count > 0 ? new DateRange(txs.Min(t => DateOnly.FromDateTime(t.PostedAt.DateTime)), txs.Max(t => DateOnly.FromDateTime(t.PostedAt.DateTime))) : null;

        var name = card is not null ? $"Сбер карта {card}" : accNum is not null ? $"Сбер счёт …{accNum[^4..]}" : "Сбер";
        var account = new ExternalAccount(accountKey, name, AccountType.Card, currency, accNum ?? card, balance);
        return Task.FromResult(new StatementParseResult(account, txs, period, warnings));
    }

    private static CounterpartyRaw ExtractCounterparty(string desc, string category)
    {
        // "Перевод на карту 2202 20** **** 1234 И. Иванов" / "СБП Перевод Иван Иванович И." / "Перевод с карты"
        var phone = Regex.Match(desc, @"(\+7|8)[\s\-]?\(?\d{3}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}");
        if (phone.Success) return new CounterpartyRaw(desc, Phone: phone.Value);
        var personSbp = Regex.Match(desc, @"(?:СБП|перевод)[^А-ЯЁ]*(?<name>[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+(?:\s+[А-ЯЁ]\.)?)", RegexOptions.IgnoreCase);
        if (personSbp.Success) return new CounterpartyRaw(personSbp.Groups["name"].Value.Trim());
        return new CounterpartyRaw(desc);
    }

    internal static bool TryAmount(string s, out decimal v)
    {
        var cleaned = s.Replace(" ", "").Replace(" ", "").Replace("−", "-").Replace(",", ".").TrimStart('+');
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
