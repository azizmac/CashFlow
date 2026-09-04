using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CashFlow.Connectors.Abstractions;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Shared;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// Формат обмена «1CClientBankExchange» (*.txt, «Экспорт в 1С») — его выгружают СберБизнес, Т-Бизнес, Альфа, ВТБ и др.
/// Содержит всё: плательщик/получатель, ИНН, счета, БИК, назначение платежа, номер документа.
/// Это лучший источник для расчётного счёта ИП/ЮЛ. Банк определяется по содержимому (Отправитель, свой БИК/банк).
/// Проверено на реальных выгрузках СберБизнеса (676 и 958 документов).
/// </summary>
public sealed class ClientBankExchangeParser : IStatementParser
{
    public string BankCode => Institution.Codes.Other; // уточняется в DetectedBankCode
    public string Code => "1c-client-bank";
    public string DisplayName => "Выписка в формате 1С (1CClientBankExchange, txt)";
    public IReadOnlyList<string> Extensions => [".txt"];

    public async Task<bool> CanParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var head = await ReadTextAsync(content, ct, maxBytes: 4096);
        return head.Contains("1CClientBankExchange", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<StatementParseResult> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var text = await ReadTextAsync(content, ct, maxBytes: int.MaxValue);
        var warnings = new List<string>();

        string? account = null, sender = null, ownBankName = null, ownBik = null;
        DateOnly? from = null, to = null;
        decimal? opening = null, closing = null;
        var inHeader = true; // до первой секции — заголовок файла; в СекцияРасчСчет даты повторяются по дням
        var txs = new List<ExternalTransaction>();
        var seenIds = new HashSet<string>();
        Dictionary<string, string>? doc = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("СекцияДокумент", StringComparison.OrdinalIgnoreCase)) { doc = new(); inHeader = false; continue; }
            if (line.StartsWith("СекцияРасчСчет", StringComparison.OrdinalIgnoreCase)) { inHeader = false; continue; }
            if (line.Equals("КонецДокумента", StringComparison.OrdinalIgnoreCase))
            {
                if (doc is not null && account is not null)
                {
                    var tx = MapDocument(doc, account, seenIds, warnings, ref ownBankName, ref ownBik);
                    if (tx is not null) txs.Add(tx);
                }
                doc = null;
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            if (doc is not null) { doc[key] = val; continue; }

            switch (key)
            {
                case "РасчСчет": account ??= val; break;
                case "ДатаНачала": if (inHeader || from is null) from = ParseDate(val) ?? from; break;
                case "ДатаКонца": if (inHeader || to is null) to = ParseDate(val) ?? to; else if (ParseDate(val) is { } d && d > to) to = d; break;
                case "НачальныйОстаток": opening ??= Dec(val); break;
                case "КонечныйОстаток": closing = Dec(val) ?? closing; break; // последний — на конец периода
                case "Отправитель": sender ??= val; break;
            }
        }

        if (account is null) throw new InvalidDataException("В файле нет секции СекцияРасчСчет / РасчСчет");
        var bankCode = DetectBank(sender, ownBankName, ownBik);
        var bankName = BankDisplayName(bankCode);
        var period = from is { } f && to is { } t ? new DateRange(f, t) : (DateRange?)null;
        var accName = $"{bankName} р/с …{account[^4..]}";
        var ext = new ExternalAccount(account, accName, AccountType.Checking, Currency.RUB, account, closing is { } c ? new Money(c, Currency.RUB) : null);
        if (txs.Count == 0) warnings.Add("В файле нет документов (СекцияДокумент).");
        return new StatementParseResult(ext, txs, period, warnings, bankCode);
    }

    private static ExternalTransaction? MapDocument(Dictionary<string, string> d, string ownAccount, HashSet<string> seenIds, List<string> warnings, ref string? ownBankName, ref string? ownBik)
    {
        string? G(params string[] keys) { foreach (var k in keys) if (d.TryGetValue(k, out var v) && v.Length > 0 && v != "0") return v; return null; }

        var amount = Dec(G("Сумма") ?? "");
        if (amount is null || amount == 0) return null;

        var payerAcc = G("ПлательщикСчет", "ПлательщикРасчСчет");
        var payeeAcc = G("ПолучательСчет", "ПолучательРасчСчет");
        var isDebit = payerAcc == ownAccount || (payeeAcc != ownAccount && G("ДатаСписано") is not null);

        var dateStr = isDebit ? G("ДатаСписано", "ДатаПоступило", "Дата") : G("ДатаПоступило", "ДатаСписано", "Дата");
        var date = dateStr is null ? null : ParseDate(dateStr);
        if (date is null) { warnings.Add($"Документ {G("Номер")}: нет даты"); return null; }

        // Свой банк — для определения банка выписки
        var ownSide = isDebit ? "Плательщик" : "Получатель";
        ownBankName ??= G(ownSide + "Банк1", ownSide + "Банк");
        ownBik ??= G(ownSide + "БИК");

        var side = isDebit ? "Получатель" : "Плательщик";
        var cpAccount = isDebit ? payeeAcc : payerAcc;
        var name = CleanName(G(side, side + "1", side + "Наименование"));
        var purpose = G("НазначениеПлатежа", "НазначениеПлатежа1");
        var bankName = G(side + "Банк1", side + "Банк");

        // Операции по бизнес-карте: контрагент в файле — отделение банка, магазин — в назначении платежа
        string desc;
        CounterpartyRaw cp;
        if (PurposeHints.IsCardSettlementAccount(cpAccount) && PurposeHints.Merchant(purpose) is { } merchant)
        {
            // ИНН и счёт здесь принадлежат банку-эквайеру, а не магазину: оставляем только название, иначе все магазины склеятся в одного контрагента
            var kind = PurposeHints.CardOperationKind(purpose);
            desc = kind is null ? merchant : $"{kind}: {merchant}";
            cp = new CounterpartyRaw(merchant, BankName: bankName);
        }
        else
        {
            desc = PurposeHints.IsBankIncomeAccount(cpAccount)
                ? (purpose is { Length: > 0 } ? Shorten(purpose, 80) : name ?? "Комиссия банка")
                : name ?? purpose ?? "Операция";
            cp = new CounterpartyRaw(name, Inn: G(side + "ИНН"), Kpp: G(side + "КПП"), Account: cpAccount, Bik: G(side + "БИК"), BankName: bankName);
        }

        // Ключ общий с SberBusinessStatementParser (XLSX/PDF): тот же документ в другом формате станет дубликатом, а не второй операцией
        var baseId = StatementIds.SberDocument(date.Value, G("Номер") ?? "0", isDebit ? -amount.Value : amount.Value, cpAccount);
        var id = baseId;
        for (var k = 2; !seenIds.Add(id); k++) id = $"{baseId}-{k}";
        var raw = string.Join('\n', d.Select(kv => $"{kv.Key}={kv.Value}"));

        return new ExternalTransaction(id, ownAccount, new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3)),
            new Money(isDebit ? -amount.Value : amount.Value, Currency.RUB), desc, cp, purpose, RawPayload: raw);
    }

    /// <summary>Определяет банк выписки по заголовку и реквизитам своей стороны.</summary>
    internal static string DetectBank(string? sender, string? ownBankName, string? ownBik)
    {
        var s = TextNormalizer.Normalize($"{sender} {ownBankName}");
        if (s.Contains("сбер")) return Institution.Codes.Sber;
        if (s.Contains("тинькофф") || s.Contains("т-банк") || s.Contains("тбанк") || s.Contains("tinkoff") || s.Contains("t-bank")) return Institution.Codes.TBank;
        if (s.Contains("альфа") || s.Contains("alfa")) return Institution.Codes.Alfa;
        if (s.Contains("втб") || s.Contains("vtb")) return Institution.Codes.Vtb;
        return ownBik switch
        {
            "044525225" => Institution.Codes.Sber,
            "044525974" => Institution.Codes.TBank,
            "044525593" => Institution.Codes.Alfa,
            "044525187" or "044525411" => Institution.Codes.Vtb,
            _ => Institution.Codes.Other,
        };
    }

    private static string BankDisplayName(string code) => code switch
    {
        Institution.Codes.Sber => "Сбер",
        Institution.Codes.TBank => "Т-Банк",
        Institution.Codes.Alfa => "Альфа-Банк",
        Institution.Codes.Vtb => "ВТБ",
        _ => "Банк",
    };

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        // В поле часто "ИНН 7707083893 ПАО СБЕРБАНК" или "770708389 ООО РОМАШКА"
        var n = Regex.Replace(name, @"^(ИНН\s*)?\d{10,12}\s*(КПП\s*\d{9}\s*)?", "", RegexOptions.IgnoreCase).Trim();
        n = n.Replace("//", " ").Trim();
        n = Regex.Replace(n, @"\s+", " ");
        return n.Length == 0 ? name.Trim() : n;
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private static DateOnly? ParseDate(string s) =>
        DateOnly.TryParseExact(s, ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Dec(string s) =>
        decimal.TryParse(Regex.Replace(s, @"\s", "").Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static async Task<string> ReadTextAsync(Stream s, CancellationToken ct, int maxBytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int read, total = 0;
        while ((read = await s.ReadAsync(buf, ct)) > 0 && total < maxBytes) { ms.Write(buf, 0, read); total += read; }
        var data = ms.ToArray();
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(data); }
        catch { return Encoding.GetEncoding(1251).GetString(data); }
    }
}
