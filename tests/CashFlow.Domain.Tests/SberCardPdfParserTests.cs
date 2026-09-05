using CashFlow.Connectors.Statements;

namespace CashFlow.Domain.Tests;

/// <summary>Разбор описаний из PDF-выписки по карте Сбера (структура строк как в реальной выписке, имена и номера вымышленные).</summary>
public class SberCardPdfParserTests
{
    [Theory]
    [InlineData("Перевод от П. Иван Сергеевич. Операция по карте ****1234", "Перевод от П. Иван Сергеевич")]
    [InlineData("Перевод из Yandex. Операция по карте ****1234", "Перевод из Yandex")]
    [InlineData("SBER*5411*MARKET MOSCOW RUS. Операция по карте ****1234", "SBER*5411*MARKET MOSCOW RUS")]
    [InlineData("COFFEE MALL MOSCOW RUS. Операция по карте ****1234", "COFFEE MALL MOSCOW RUS")]
    [InlineData("Перевод для П. Иван Сергеевич. Операция по карте", "Перевод для П. Иван Сергеевич")]
    [InlineData("Банкомат №123456. Операция по карте ****1234", "Банкомат №123456")]
    public void Cleans_card_suffix(string raw, string expected) =>
        Assert.Equal(expected, SberPdfStatementParser.CleanDescription(raw));

    [Fact]
    public void Extracts_person_from_transfer()
    {
        var cp = SberPdfStatementParser.ExtractCounterparty("Перевод для П. Иван Сергеевич", "Перевод с карты", out var mcc);
        Assert.Equal("П. Иван Сергеевич", cp.Name);
        Assert.Null(mcc);
    }

    [Fact]
    public void Extracts_bank_from_sbp_transfer()
    {
        var cp = SberPdfStatementParser.ExtractCounterparty("Перевод из T-Bank", "Перевод СБП", out _);
        Assert.Equal("T-Bank", cp.Name);
    }

    [Fact]
    public void Extracts_mcc_and_merchant()
    {
        var cp = SberPdfStatementParser.ExtractCounterparty("SBER*5411*MARKET MOSCOW RUS", "Прочие расходы", out var mcc);
        Assert.Equal("5411", mcc);
        Assert.Equal("MARKET MOSCOW RUS", cp.Name);
    }

    [Fact]
    public void Strips_terminal_code_for_qr_payment()
    {
        var cp = SberPdfStatementParser.ExtractCounterparty("G425 Магазин", "Оплата по QR–коду СБП", out _);
        Assert.Equal("Магазин", cp.Name);
    }

    [Fact]
    public void Phone_transfer_keeps_phone()
    {
        var cp = SberPdfStatementParser.ExtractCounterparty("Перевод по номеру телефона +7 900 123-45-67", "Перевод СБП", out _);
        Assert.Equal("+7 900 123-45-67", cp.Phone);
    }

    [Theory]
    [InlineData("+5 000,00", 5000)]
    [InlineData("1 747,00", 1747)]
    [InlineData("−250,50", -250.5)]
    public void Parses_amounts(string raw, decimal expected)
    {
        Assert.True(SberPdfStatementParser.TryAmount(raw, out var v));
        Assert.Equal(expected, v);
    }
}
