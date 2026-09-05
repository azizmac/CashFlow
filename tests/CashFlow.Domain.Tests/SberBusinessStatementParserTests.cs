using CashFlow.Connectors.Statements;
using CashFlow.Domain.Connections;
using ClosedXML.Excel;

namespace CashFlow.Domain.Tests;

/// <summary>Расширенная XLSX-выписка СберКазначейства — структура повторяет реальный файл (все реквизиты, суммы и названия вымышленные).</summary>
public class SberBusinessStatementParserTests
{
    private const string Own = "40702810100000000001";

    private static MemoryStream ExtendedWorkbook()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Расширенная выписка");
        ws.Cell("B2").Value = "03.08.2026 СберКазначейство. Контроль состояния счетов.";
        ws.Cell("B4").Value = "Дата формирования выписки 03.08.2026 в 11:44:27";
        ws.Cell("B5").Value = $"ВЫПИСКА ОПЕРАЦИЙ ПО ЛИЦЕВОМУ СЧЁТУ {Own}";
        ws.Cell("B6").Value = "ОБЩЕСТВО С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ \"ТЕСТ\"";
        ws.Cell("B7").Value = "за период с 01.07.2026 по 31.07.2026";
        ws.Cell("B8").Value = "Российский рубль";
        ws.Cell("B11").Value = "Дата проводки"; ws.Cell("C11").Value = "Счёт"; ws.Cell("E11").Value = "Сумма по дебету"; ws.Cell("F11").Value = "Сумма по кредиту\t";
        ws.Cell("G11").Value = "№ документа\t"; ws.Cell("H11").Value = "ВО"; ws.Cell("I11").Value = "Банк (БИК и наименование)\t"; ws.Cell("J11").Value = "Назначение платежа";
        ws.Cell("C12").Value = "Дебет"; ws.Cell("D12").Value = "Кредит";

        // Поступление от ООО
        ws.Cell("B13").Value = "01.07.2026";
        ws.Cell("C13").Value = "40702810100000000456\n7700000001\nООО \"Доставка Еды\"";
        ws.Cell("D13").Value = $"{Own}\n7700000002\nООО \"ТЕСТ\"";
        ws.Cell("F13").Value = 499.28;
        ws.Cell("G13").Value = "85519"; ws.Cell("H13").Value = "01";
        ws.Cell("I13").Value = "БИК 044525225\nПАО Сбербанк";
        ws.Cell("J13").Value = "Перечисление денежных средств по договору 12/26";

        // Оплата ИП
        ws.Cell("B14").Value = "02.07.2026";
        ws.Cell("C14").Value = $"{Own}\n7700000002\nООО \"ТЕСТ\"";
        ws.Cell("D14").Value = "40802810100000000789\n770000000003\nИП Иванов Иван Иванович";
        ws.Cell("E14").Value = 4760;
        ws.Cell("G14").Value = "1291"; ws.Cell("H14").Value = "01";
        ws.Cell("I14").Value = "БИК 044525801\nООО \"Бланк банк\"";
        ws.Cell("J14").Value = "КЗ Счет № 402 от 21.05.2026 за перчатки";

        // Покупка по бизнес-карте: контрагент — отделение банка, магазин в назначении
        ws.Cell("B15").Value = "02.07.2026";
        ws.Cell("C15").Value = $"{Own}\n7700000002\nООО \"ТЕСТ\"";
        ws.Cell("D15").Value = "30232810900000000000\n7707083893\nОТДЕЛЕНИЕ N0001 ПАО СБЕРБАНК";
        ws.Cell("E15").Value = 300;
        ws.Cell("G15").Value = "108"; ws.Cell("H15").Value = "17";
        ws.Cell("I15").Value = "БИК 044525225\nОТДЕЛЕНИЕ N0001 ПАО СБЕРБАНК";
        ws.Cell("J15").Value = "Покупка PURCHASE_CB в ТУ Сбербанка DONER по карте MIR 2202 за 2026-07-02. Держатель ИВАНОВ. 1310 КА_1 Чек он-лайн №108 от 02.07.2026";

        // Зарплата физлицу без ИНН: номер счёта склеен с именем в одной строке
        ws.Cell("B16").Value = "03.07.2026";
        ws.Cell("C16").Value = $"{Own}\n7700000002\nООО \"ТЕСТ\"";
        ws.Cell("D16").Value = "40817810100000000004Петрова Мария Ивановна";
        ws.Cell("E16").Value = 4271.71;
        ws.Cell("G16").Value = "1375"; ws.Cell("H16").Value = "01";
        ws.Cell("I16").Value = "БИК 044525225\nОТДЕЛЕНИЕ N0001 ПАО СБЕРБАНК";
        ws.Cell("J16").Value = "Зарплата за июнь 2026";

        ws.Cell("B17").Value = "б/с 40702"; ws.Cell("D17").Value = "Дебет"; ws.Cell("F17").Value = "Кредит"; ws.Cell("J17").Value = "Всего";
        ws.Cell("B18").Value = "Количество операций"; ws.Cell("D18").Value = 2; ws.Cell("F18").Value = 1; ws.Cell("J18").Value = 3;
        ws.Cell("B19").Value = "Входящий остаток"; ws.Cell("D19").Value = "0,00"; ws.Cell("F19").Value = "100 000,00 (П)"; ws.Cell("J19").Value = "01.07.2026";
        ws.Cell("B20").Value = "Итого оборотов"; ws.Cell("D20").Value = 5060; ws.Cell("F20").Value = 499.28;
        // Разряды разделены узким пробелом U+202F, как в реальном файле; в конце строки дата, которую нельзя принять за сумму
        ws.Cell("B21").Value = "Исходящий остаток"; ws.Cell("D21").Value = "0,00"; ws.Cell("F21").Value = "95 439,28 (П)"; ws.Cell("J21").Value = "31.07.2026";

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Parses_extended_xlsx_with_counterparties()
    {
        var p = new SberBusinessStatementParser();
        using var ms = ExtendedWorkbook();
        Assert.True(await p.CanParseAsync(ms, "extended.xlsx", default));
        ms.Position = 0;
        var r = await p.ParseAsync(ms, "extended.xlsx", default);

        Assert.Equal(Institution.Codes.Sber, r.DetectedBankCode);
        Assert.Equal(Own, r.Account.AccountNumber);
        Assert.Contains("ООО \"ТЕСТ\"", r.Account.Name);
        Assert.Equal(95439.28m, r.Account.Balance!.Amount);   // «95 439,28 (П)», а не дата 31.07.2026
        Assert.Equal(new DateOnly(2026, 7, 1), r.Period!.Value.From);
        Assert.Equal(new DateOnly(2026, 7, 31), r.Period!.Value.To);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("Краткий формат"));
        Assert.Equal(4, r.Transactions.Count);

        var income = r.Transactions[0];
        Assert.Equal(499.28m, income.Amount.Amount);
        Assert.Equal("ООО \"Доставка Еды\"", income.Counterparty.Name);
        Assert.Equal("7700000001", income.Counterparty.Inn);
        Assert.Equal("40702810100000000456", income.Counterparty.Account);
        Assert.Equal("044525225", income.Counterparty.Bik);
        Assert.Contains("Перечисление", income.Purpose);

        var payment = r.Transactions[1];
        Assert.Equal(-4760m, payment.Amount.Amount);
        Assert.Equal("ИП Иванов Иван Иванович", payment.Counterparty.Name);
        Assert.Equal("770000000003", payment.Counterparty.Inn);
        Assert.Equal("ООО \"Бланк банк\"", payment.Counterparty.BankName);

        var card = r.Transactions[2];
        Assert.Equal(-300m, card.Amount.Amount);
        Assert.Equal("DONER", card.Counterparty.Name);
        Assert.Equal("Покупка: DONER", card.Description);

        var salary = r.Transactions[3];
        Assert.Equal(-4271.71m, salary.Amount.Amount);
        Assert.Equal("Петрова Мария Ивановна", salary.Counterparty.Name);
        Assert.Equal("40817810100000000004", salary.Counterparty.Account);
        Assert.Null(salary.Counterparty.Inn);
        Assert.Equal("20260703-1375-4271.71-D-0004", salary.ExternalId);   // тот же ключ даёт ClientBankExchangeParser

        Assert.Equal(4, r.Transactions.Select(t => t.ExternalId).Distinct().Count());
    }

    [Theory]
    [InlineData("Покупка PURCHASE_CB в ТУ Сбербанка SUPERMARKET 0001 по карте MIR 2202 за 2026-04-17.", "SUPERMARKET 0001", "Покупка")]
    [InlineData("Частичная отмена покупки PURCHASE_CB в ТУ Сбербанка AZS 01 по карте MIR 2202 за 2026-08-27.", "AZS 01", "Частичная отмена покупки")]
    [InlineData("Корректировка. Отмена покупки PURCHASE_CB в ТУ Сбербанка MARKET2 по карте MIR 2202 за 2026-06-28.", "MARKET2", "Отмена покупки")]
    [InlineData("Покупка в ТУ Сбербанка TELECOM E001 по карте MIR 2202 за 2026-04-27.", "TELECOM E001", "Покупка")]
    [InlineData("Покупка PURCHASE_CB в ТУ CAFE PRIMER по карте MIR 2202 за 2026-04-19.", "CAFE PRIMER", "Покупка")]
    [InlineData("Зачисление средств по операциям эквайринга. Мерчант №000000000001.", "Эквайринг Сбер", null)]
    [InlineData("Комиссия за ведение счета", null, null)]
    public void Extracts_merchant_from_purpose(string purpose, string? merchant, string? kind)
    {
        Assert.Equal(merchant, PurposeHints.Merchant(purpose));
        Assert.Equal(kind, PurposeHints.CardOperationKind(purpose));
    }
}
