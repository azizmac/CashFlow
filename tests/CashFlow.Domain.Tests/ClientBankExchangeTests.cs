using System.Text;
using CashFlow.Connectors.Statements;
using CashFlow.Domain.Connections;

namespace CashFlow.Domain.Tests;

public class ClientBankExchangeTests
{
    // Структура повторяет реальную выгрузку СберБизнеса: заголовок с периодом, затем СекцияРасчСчет на каждый день, затем документы.
    private const string Sample = """
        1CClientBankExchange
        ВерсияФормата=1.03
        Кодировка=Windows
        Отправитель=Сбербанк Бизнес Онлайн
        Получатель=
        ДатаСоздания=03.09.2026
        ДатаНачала=01.08.2026
        ДатаКонца=28.08.2026
        РасчСчет=40802810000000000001
        СекцияРасчСчет
        ДатаНачала=01.08.2026
        ДатаКонца=01.08.2026
        НачальныйОстаток=1000.00
        РасчСчет=40802810000000000001
        ВсегоСписано=0
        ВсегоПоступило=0
        КонечныйОстаток=1000.00
        КонецРасчСчет
        СекцияРасчСчет
        ДатаНачала=28.08.2026
        ДатаКонца=28.08.2026
        НачальныйОстаток=51000.00
        РасчСчет=40802810000000000001
        ВсегоСписано=250.00
        ВсегоПоступило=0
        КонечныйОстаток=50750.00
        КонецРасчСчет
        СекцияДокумент=Платежное поручение
        Номер=101
        Дата=13.08.2026
        Сумма=250000.00
        ПлательщикСчет=40702810000000000002
        ДатаСписано=
        Плательщик=ИНН 7700000005 ООО "РОМАШКА"
        ПлательщикИНН=7700000005
        ПлательщикКПП=770401001
        ПлательщикБИК=044525225
        ПлательщикБанк1=ПАО СБЕРБАНК
        ПолучательСчет=40802810000000000001
        ДатаПоступило=13.08.2026
        Получатель=ИП ИВАНОВ И.И.
        ПолучательКПП=0
        ПолучательБанк1=ОТДЕЛЕНИЕ N0001 ПАО СБЕРБАНК
        ПолучательБИК=044525225
        НазначениеПлатежа=Оплата по договору 12 за услуги разработки. НДС не облагается
        КонецДокумента
        СекцияДокумент=Платежное поручение
        Номер=102
        Дата=26.08.2026
        Сумма=5000.00
        ПлательщикСчет=40802810000000000001
        ДатаСписано=26.08.2026
        Плательщик=ИП ИВАНОВ И.И.
        ПлательщикКПП=0
        ПолучательСчет=40817810000000000003
        ДатаПоступило=
        Получатель=ИВАНОВ ИВАН ИВАНОВИЧ
        ПолучательИНН=123456789012
        ПолучательКПП=0
        НазначениеПлатежа=Перевод собственных средств
        КонецДокумента
        СекцияДокумент=Платежное поручение
        Номер=103
        Дата=28.08.2026
        Сумма=250.00
        ПлательщикСчет=40802810000000000001
        ДатаСписано=28.08.2026
        Плательщик=ИП ИВАНОВ И.И.
        ПолучательСчет=30232810900000000000
        Получатель=Отделение №0001 ПАО Сбербанк
        ПолучательИНН=7707083893
        ПолучательБИК=044525225
        ВидОплаты=17
        НазначениеПлатежа=Покупка PURCHASE_CB в ТУ Сбербанка SUPERMARKET 0001 по карте MIR 2202 за 2026-08-28. Держатель ИВАНОВ ИВАН ИВАНОВИЧ. 1310 КА_1 Чек он-лайн №103 от 28.08.2026
        КонецДокумента
        КонецФайла
        """;

    [Fact]
    public async Task Parses_documents_with_counterparties()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var p = new ClientBankExchangeParser();
        using var ms = new MemoryStream(Encoding.GetEncoding(1251).GetBytes(Sample));
        Assert.True(await p.CanParseAsync(ms, "kl_to_1c.txt", default));
        ms.Position = 0;
        var r = await p.ParseAsync(ms, "kl_to_1c.txt", default);

        Assert.Equal("40802810000000000001", r.Account.AccountNumber);
        Assert.Equal(50750m, r.Account.Balance!.Amount);          // последний КонечныйОстаток
        Assert.Equal(Institution.Codes.Sber, r.DetectedBankCode);  // по «Отправитель=Сбербанк Бизнес Онлайн»
        Assert.Equal("Сбер р/с …0001", r.Account.Name);
        Assert.Equal(new DateOnly(2026, 8, 1), r.Period!.Value.From);  // из заголовка, а не из последней дневной секции
        Assert.Equal(new DateOnly(2026, 8, 28), r.Period!.Value.To);
        Assert.Equal(3, r.Transactions.Count);

        var income = r.Transactions[0];
        Assert.Equal(250000m, income.Amount.Amount);
        Assert.Equal("ООО \"РОМАШКА\"", income.Counterparty.Name);
        Assert.Equal("7700000005", income.Counterparty.Inn);
        Assert.Equal("770401001", income.Counterparty.Kpp);
        Assert.Contains("Оплата по договору", income.Purpose);

        var outgoing = r.Transactions[1];
        Assert.Equal(-5000m, outgoing.Amount.Amount);
        Assert.Equal("123456789012", outgoing.Counterparty.Inn);
        Assert.Null(outgoing.Counterparty.Kpp);                    // «0» не считается КПП
        Assert.Equal("40817810000000000003", outgoing.Counterparty.Account);

        var card = r.Transactions[2];
        Assert.Equal(-250m, card.Amount.Amount);
        Assert.Equal("SUPERMARKET 0001", card.Counterparty.Name);  // магазин из назначения, а не отделение банка
        Assert.Equal("Покупка: SUPERMARKET 0001", card.Description);
        Assert.Null(card.Counterparty.Inn);                        // ИНН банка-эквайера магазину не приписываем
        Assert.Null(card.Counterparty.Account);

        Assert.Equal(3, r.Transactions.Select(t => t.ExternalId).Distinct().Count());
    }

    [Theory]
    [InlineData("Сбербанк Бизнес Онлайн", null, null, Institution.Codes.Sber)]
    [InlineData(null, "АО \"ТБанк\"", "044525974", Institution.Codes.TBank)]
    [InlineData(null, "ФИЛИАЛ \"ЕКАТЕРИНБУРГСКИЙ\" АО \"АЛЬФАБАНК\"", "046577964", Institution.Codes.Alfa)]
    [InlineData(null, null, "044525187", Institution.Codes.Vtb)]
    [InlineData(null, "ООО \"Банк Точка\"", "044525104", Institution.Codes.Other)]
    public void Detects_bank(string? sender, string? bank, string? bik, string expected) =>
        Assert.Equal(expected, ClientBankExchangeParser.DetectBank(sender, bank, bik));

    static ClientBankExchangeTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
