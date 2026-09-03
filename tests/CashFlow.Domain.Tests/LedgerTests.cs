using CashFlow.Domain.Ledger;
using CashFlow.Domain.Ledger.Services;
using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Currency_FromStatement_maps_symbols()
    {
        Assert.Equal(Currency.RUB, Currency.FromStatement("₽"));
        Assert.Equal(Currency.RUB, Currency.FromStatement("RUR"));
        Assert.Equal(Currency.RUB, Currency.FromStatement("643"));
        Assert.Equal(Currency.USD, Currency.FromStatement("$"));
    }

    [Fact]
    public void Money_arithmetic_requires_same_currency()
    {
        var a = new Money(10, Currency.RUB);
        var b = new Money(5, Currency.RUB);
        Assert.Equal(15, (a + b).Amount);
        Assert.Throws<InvalidOperationException>(() => a + new Money(1, Currency.USD));
    }
}

public class DedupeTests
{
    [Fact]
    public void Same_content_gives_same_key_and_external_id_wins()
    {
        var acc = Guid.NewGuid();
        var k1 = DedupeKey.Compute(acc, new DateOnly(2026, 9, 1), -100m, "RUB", "магазин", null);
        var k2 = DedupeKey.Compute(acc, new DateOnly(2026, 9, 1), -100m, "RUB", "магазин", null);
        var k3 = DedupeKey.Compute(acc, new DateOnly(2026, 9, 1), -100m, "RUB", "аптека", null);
        var e1 = DedupeKey.Compute(acc, new DateOnly(2026, 9, 1), -100m, "RUB", "магазин", "op-1");
        var e2 = DedupeKey.Compute(acc, new DateOnly(2026, 9, 2), -999m, "RUB", "другое", "op-1");
        Assert.Equal(k1, k2);
        Assert.NotEqual(k1, k3);
        Assert.Equal(e1, e2);
    }
}

public class CounterpartyMatcherTests
{
    private static Transaction Tx(Guid acc, decimal amount, string desc, CounterpartyRaw? cp = null, DateTimeOffset? at = null)
    {
        var t = at ?? new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3));
        return new Transaction(acc, t, new Money(amount, Currency.RUB), desc, ConnectorType.StatementImport,
            DedupeKey.Compute(acc, DateOnly.FromDateTime(t.DateTime), amount, "RUB", desc, null), cp);
    }

    [Fact]
    public void Matches_by_inn_then_by_alias_and_creates_new()
    {
        var m = new CounterpartyMatcher("u", [], [], [], []);
        var a = m.Resolve(new CounterpartyRaw("ООО \"Ромашка Маркет\"", Inn: "7700000005"))!;
        Assert.True(a.Created);
        Assert.Equal(CounterpartyKind.Company, a.Counterparty.Kind);

        var b = m.Resolve(new CounterpartyRaw("ROMASHKA", Inn: "7700000005"))!;
        Assert.False(b.Created);
        Assert.Same(a.Counterparty, b.Counterparty);
        Assert.Contains("romashka", b.Counterparty.Aliases);

        var c = m.Resolve(new CounterpartyRaw("romashka"))!;
        Assert.False(c.Created);
        Assert.Equal("alias", c.Reason);

        var d = m.Resolve(new CounterpartyRaw("Ромашка Маркет"))!;
        Assert.False(d.Created);
        Assert.Equal("name", d.Reason);
    }

    [Fact]
    public void Own_account_resolves_to_self()
    {
        var m = new CounterpartyMatcher("u", [], ["40802810000000000001"], ["123456789012"], []);
        var r = m.Resolve(new CounterpartyRaw("ИП Иванов", Inn: "123456789012"))!;
        Assert.Equal(CounterpartyKind.Self, r.Counterparty.Kind);
    }

    [Fact]
    public void Phone_normalization()
    {
        Assert.Equal("+79001234567", Counterparty.NormalizePhone("8 (900) 123-45-67"));
        Assert.Equal("+79001234567", Counterparty.NormalizePhone("+7 900 123 45 67"));
        Assert.Equal("+79001234567", Counterparty.NormalizePhone("9001234567"));
    }

    [Fact]
    public void TransferMatcher_links_opposite_amounts_between_accounts()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var t1 = Tx(a, -5000, "Перевод между своими счетами");
        var t2 = Tx(b, 5000, "Пополнение", at: new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.FromHours(3)));
        var t3 = Tx(b, -300, "Магазин");
        var links = new TransferMatcher().FindPairs([t1, t2, t3]);
        Assert.Single(links);
        Assert.Equal(TransactionKind.Transfer, t1.Kind);
        Assert.Equal(TransactionKind.Transfer, t2.Kind);
        Assert.Null(t3.TransferLinkId);
    }

    [Fact]
    public void User_category_is_never_overwritten_by_auto()
    {
        var t = Tx(Guid.NewGuid(), -100, "x");
        var userCat = Guid.NewGuid();
        t.SetCategoryByUser(userCat);
        Assert.False(t.Categorize(Guid.NewGuid(), CategorySource.Rule, 1m));
        Assert.Equal(userCat, t.CategoryId);
    }

    [Fact]
    public void Low_confidence_becomes_proposal()
    {
        var t = Tx(Guid.NewGuid(), -100, "x");
        var cat = Guid.NewGuid();
        t.Categorize(cat, CategorySource.Ai, 0.4m);
        Assert.Null(t.CategoryId);
        Assert.Equal(cat, t.ProposedCategoryId);
        t.AcceptProposal();
        Assert.Equal(cat, t.CategoryId);
        Assert.True(t.ReviewedByUser);
    }

    [Fact]
    public void Rule_regex_matches_normalized_description()
    {
        var rule = new CategorizationRule(null, RuleField.Description, RuleMatch.Regex, @"ларек|киоск", Guid.NewGuid(), 10, RuleOrigin.System);
        Assert.True(rule.Matches("SUPERMARKET Магазин 1234"));
        Assert.False(rule.Matches("Яндекс Такси"));
    }
}
