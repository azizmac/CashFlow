using System.Text;
using CashFlow.Connectors.Statements;

namespace CashFlow.Domain.Tests;

public class TBankParserTests
{
    private const string Csv =
        "Дата операции;Дата платежа;Номер карты;Статус;Сумма операции;Валюта операции;Сумма платежа;Валюта платежа;Кэшбэк;Категория;MCC;Описание;Бонусы (включая кэшбэк);Округление на инвесткопилку;Сумма операции с округлением\n" +
        "01.09.2026 12:34:56;02.09.2026;*1234;OK;-1250,50;RUB;-1250,50;RUB;12;Супермаркеты;5411;Магазин;12;0;1250,50\n" +
        "01.09.2026 18:00:00;01.09.2026;*1234;OK;+50000,00;RUB;+50000,00;RUB;;Переводы;;Перевод по номеру телефона +7 900 123-45-67;;;50000,00\n" +
        "03.09.2026 09:00:00;;*1234;FAILED;-100,00;RUB;-100,00;RUB;;Прочее;;Отклонено;;;100,00\n";

    [Fact]
    public async Task Parses_csv_export()
    {
        var p = new TBankOperationsParser();
        using var ms = new MemoryStream(Encoding.GetEncoding(1251).GetBytes(Csv));
        Assert.True(await p.CanParseAsync(ms, "operations.csv", default));
        ms.Position = 0;
        var r = await p.ParseAsync(ms, "operations.csv", default);

        Assert.Equal(2, r.Transactions.Count);
        Assert.Equal("*1234", r.Account.AccountNumber);
        var t0 = r.Transactions[0];
        Assert.Equal(-1250.50m, t0.Amount.Amount);
        Assert.Equal("5411", t0.Mcc);
        Assert.Equal("Супермаркеты", t0.Purpose);
        var t1 = r.Transactions[1];
        Assert.Equal(50000m, t1.Amount.Amount);
        Assert.Equal("+7 900 123-45-67", t1.Counterparty.Phone);
        Assert.Equal(new DateOnly(2026, 9, 1), r.Period!.Value.From);
    }

    static TBankParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
