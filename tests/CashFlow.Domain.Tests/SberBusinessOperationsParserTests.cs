using CashFlow.Connectors.Statements;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Ledger.Services;
using ClosedXML.Excel;

namespace CashFlow.Domain.Tests;

/// <summary>Экспорт «Операции» из СберБизнеса — структура повторяет реальный файл.</summary>
public class SberBusinessOperationsParserTests
{
    private static MemoryStream Workbook()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("40802810700000000001");
        string[] header = ["Номер", "Номер счёта", "Дата", "Контрагент cчёт", "Контрагент", "Поступление", "Валюта", "Списание", "Валюта", "Назначение"];
        for (var i = 0; i < header.Length; i++) ws.Cell(1, i + 1).Value = header[i];

        void Row(int r, string doc, string date, string cpAcc, string cp, double? credit, double? debit, string purpose)
        {
            ws.Cell(r, 1).Value = doc; ws.Cell(r, 2).Value = "40802.810.7.00000000001"; ws.Cell(r, 3).Value = date;
            ws.Cell(r, 4).Value = cpAcc; ws.Cell(r, 5).Value = cp;
            if (credit is { } c) { ws.Cell(r, 6).Value = c; ws.Cell(r, 7).Value = "RUB"; }
            if (debit is { } d) { ws.Cell(r, 8).Value = d; ws.Cell(r, 9).Value = "RUB"; }
            ws.Cell(r, 10).Value = purpose;
        }
        Row(2, "573", "13.04.2026", "40702.810.1.00000000123", "ООО \"Ромашка\"", 250000, null, "Оплата по договору б\\н от 10.11.2025 за услуги");
        Row(3, "658568", "17.04.2026", "30232.810.9.00020117000", "Отделение №0001 ПАО Сбербанк", null, 69.99, "Покупка PURCHASE_CB в ТУ Сбербанка SUPERMARKET 0001 по карте MIR 2202 за 2026-04-17. Держатель");
        Row(4, "484445", "01.05.2026", "47423.810.9.72012238225", "ПАО Сбербанк", null, 2400, "Комиссия за ведение счета 40802810700000000001 по договору РКО");

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Parses_operations_export()
    {
        var p = new SberBusinessOperationsParser();
        using var ms = Workbook();
        var name = "СберБизнес_Операции_за_2025_09_01_2026_05_04.xlsx";
        Assert.True(await p.CanParseAsync(ms, name, default));
        ms.Position = 0;
        var r = await p.ParseAsync(ms, name, default);

        Assert.Equal(Institution.Codes.Sber, r.DetectedBankCode);
        Assert.Equal("40802810700000000001", r.Account.AccountNumber);
        Assert.Equal(new DateOnly(2025, 9, 1), r.Period!.Value.From);   // из имени файла
        Assert.Equal(new DateOnly(2026, 5, 4), r.Period!.Value.To);
        Assert.Equal(3, r.Transactions.Count);

        var income = r.Transactions[0];
        Assert.Equal(250000m, income.Amount.Amount);
        Assert.Equal("ООО \"Ромашка\"", income.Counterparty.Name);
        Assert.Equal("40702810100000000123", income.Counterparty.Account);   // точки убраны
        Assert.Equal("20260413-573-250000.00-C-0123", income.ExternalId);   // тот же ключ, что у выписки и 1С

        var card = r.Transactions[1];
        Assert.Equal(-69.99m, card.Amount.Amount);
        Assert.Equal("SUPERMARKET 0001", card.Counterparty.Name);
        Assert.Equal("Покупка: SUPERMARKET 0001", card.Description);

        var fee = r.Transactions[2];
        Assert.Equal(-2400m, fee.Amount.Amount);
        Assert.StartsWith("Комиссия за ведение счета", fee.Description);
        Assert.Equal("ПАО Сбербанк", fee.Counterparty.Name);
    }

    [Fact]
    public void Short_statement_does_not_match_operations_parser()
    {
        Assert.False(SberBusinessOperationsParser_CanParseShortHeader());
    }

    private static bool SberBusinessOperationsParser_CanParseShortHeader()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("x");
        ws.Cell("B16").Value = "Дата"; ws.Cell("O16").Value = "Сумма по дебету"; ws.Cell("P16").Value = "Сумма по кредиту";
        using var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        return new SberBusinessOperationsParser().CanParseAsync(ms, "x.xlsx", default).Result;
    }

    [Theory]
    [InlineData("Организация (поступление) · 0123", true)]
    [InlineData("Бизнес-карта (покупка) · 7000", true)]
    [InlineData("Счёт 61501…0002 БИК 044525225", true)]
    [InlineData("ООО \"Ромашка\"", false)]
    [InlineData("SUPERMARKET 0001", false)]
    public void Detects_placeholder_names(string name, bool expected) => Assert.Equal(expected, CounterpartyMatcher.IsPlaceholderName(name));

    [Fact]
    public void Placeholder_counterparty_is_renamed_when_real_name_arrives()
    {
        var placeholder = new Counterparty("u", "Организация (поступление) · 0123", CounterpartyKind.Unknown);
        placeholder.AddAccount("40702810100000000123");
        var matcher = new CounterpartyMatcher("u", [placeholder], [], [], []);

        var m = matcher.Resolve(new CounterpartyRaw("ООО \"Ромашка\"", Account: "40702810100000000123"));

        Assert.NotNull(m);
        Assert.False(m!.Created);
        Assert.Same(placeholder, m.Counterparty);
        Assert.Equal("ООО \"Ромашка\"", placeholder.DisplayName);
        Assert.Equal(CounterpartyKind.Company, placeholder.Kind);
    }
}
