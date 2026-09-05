using System.Globalization;
using CashFlow.Domain.Ledger;

namespace CashFlow.UI.Services;

/// <summary>Мелкие помощники представления: цвета, инициалы, форматы сумм.</summary>
public static class Ui
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Сумма без копеек с пробелами: 61 240.</summary>
    public static string N0(decimal v) => Math.Round(v).ToString("N0", Ru).Replace(' ', ' ');
    /// <summary>Сумма с копейками: 4 290,00.</summary>
    public static string N2(decimal v) => v.ToString("N2", Ru).Replace(' ', ' ');
    /// <summary>Со знаком: +184 000 / −12 000.</summary>
    public static string Signed(decimal v) => v < 0 ? "−" + N0(-v) : "+" + N0(v);
    public static string Signed2(decimal v) => v < 0 ? "−" + N2(-v) : "+" + N2(v);
    public static string Rub(decimal v) => N0(v) + " ₽";

    public static string Initial(string? name)
    {
        var s = (name ?? "").Trim().TrimStart('"', '«', '\'');
        foreach (var prefix in new[] { "ООО ", "ИП ", "АО ", "ПАО ", "ЗАО " })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && s.Length > prefix.Length) { s = s[prefix.Length..].TrimStart('"', '«'); break; }
        return s.Length == 0 ? "?" : s[..1].ToUpperInvariant();
    }

    /// <summary>Устойчивый оттенок по имени (oklch), как цветные аватары в макете.</summary>
    public static string HueColor(string? name)
    {
        var h = Hash(name) % 360;
        return $"oklch(.6 .16 {h})";
    }

    public static bool IsLightHue(string? name) { var h = Hash(name) % 360; return h is >= 70 and <= 110; }

    private static uint Hash(string? s)
    {
        uint h = 2166136261;
        foreach (var ch in (s ?? "").ToLowerInvariant()) { h ^= ch; h *= 16777619; }
        return h;
    }

    /// <summary>Фон иконки категории: полупрозрачный цвет категории или нейтральный.</summary>
    public static string CategoryTint(string? color, int index)
    {
        if (!string.IsNullOrWhiteSpace(color) && color.StartsWith('#') && color.Length == 7)
            return $"color-mix(in srgb, {color} 22%, transparent)";
        string[] tints = ["rgba(76,175,80,.22)", "rgba(255,152,0,.2)", "rgba(33,150,243,.2)", "rgba(139,195,74,.2)", "rgba(96,125,139,.22)", "rgba(156,39,176,.2)", "rgba(0,188,212,.2)"];
        return tints[index % tints.Length];
    }

    /// <summary>Цвет полосы категории.</summary>
    public static string CategoryBar(string? color, int index)
    {
        if (!string.IsNullOrWhiteSpace(color) && color.StartsWith('#') && color.Length == 7) return color;
        string[] bars = ["oklch(.72 .14 150)", "oklch(.75 .15 70)", "oklch(.7 .14 250)", "oklch(.74 .14 130)", "oklch(.68 .06 240)", "oklch(.6 .12 300)", "oklch(.7 .12 200)"];
        return bars[index % bars.Length];
    }

    public static string KindLabel(CounterpartyKind k) => k switch
    {
        CounterpartyKind.Person => "человек", CounterpartyKind.Company => "компания", CounterpartyKind.SoleProprietor => "ИП",
        CounterpartyKind.Bank => "банк", CounterpartyKind.Government => "государство", CounterpartyKind.Merchant => "магазин", CounterpartyKind.Self => "я", _ => "не определён"
    };

    public static string AccountTypeLabel(AccountType t) => t switch
    {
        AccountType.Checking => "Расчётный", AccountType.Card => "Карта", AccountType.Savings => "Накопительный", AccountType.Deposit => "Вклад",
        AccountType.CreditCard => "Кредитная карта", AccountType.Loan => "Кредит", AccountType.Brokerage => "Брокерский", AccountType.Iis => "ИИС",
        AccountType.Cash => "Наличные", AccountType.EWallet => "Кошелёк", _ => t.ToString()
    };

    public static string SourceLabel(CategorySource s) => s switch
    {
        CategorySource.User => "вручную", CategorySource.Rule => "правило", CategorySource.Mcc => "по MCC", CategorySource.Ai => "ИИ", CategorySource.Counterparty => "по контрагенту", _ => ""
    };

    public static string DayLabel(DateOnly d)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var text = d.ToString("d MMMM", Ru);
        if (d == today) return "Сегодня · " + text;
        if (d == today.AddDays(-1)) return "Вчера · " + text;
        return d.Year == today.Year ? text : d.ToString("d MMMM yyyy", Ru);
    }
}
