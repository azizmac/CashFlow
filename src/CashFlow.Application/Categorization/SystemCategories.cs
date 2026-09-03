using CashFlow.Domain.Ledger;

namespace CashFlow.Application.Categorization;

/// <summary>Системный набор категорий и MCC-маппинг. Сидируется при старте.</summary>
public static class SystemCategories
{
    public sealed record Def(string Code, string Name, CategoryKind Kind, string Icon, string Color, string? ParentCode = null);

    public static readonly IReadOnlyList<Def> All =
    [
        // Расходы
        new("groceries", "Продукты", CategoryKind.Expense, "🛒", "#4CAF50"),
        new("cafe", "Кафе и рестораны", CategoryKind.Expense, "🍽️", "#FF9800"),
        new("transport", "Транспорт", CategoryKind.Expense, "🚌", "#2196F3"),
        new("taxi", "Такси", CategoryKind.Expense, "🚕", "#FFC107", "transport"),
        new("fuel", "Топливо", CategoryKind.Expense, "⛽", "#795548", "transport"),
        new("housing", "Жильё и ЖКУ", CategoryKind.Expense, "🏠", "#607D8B"),
        new("telecom", "Связь и интернет", CategoryKind.Expense, "📱", "#9C27B0"),
        new("health", "Здоровье", CategoryKind.Expense, "💊", "#E91E63"),
        new("clothing", "Одежда", CategoryKind.Expense, "👕", "#3F51B5"),
        new("entertainment", "Развлечения", CategoryKind.Expense, "🎬", "#FF5722"),
        new("subscriptions", "Подписки", CategoryKind.Expense, "🔁", "#00BCD4"),
        new("electronics", "Техника", CategoryKind.Expense, "💻", "#455A64"),
        new("marketplaces", "Маркетплейсы", CategoryKind.Expense, "📦", "#8BC34A"),
        new("education", "Образование", CategoryKind.Expense, "🎓", "#009688"),
        new("travel", "Путешествия", CategoryKind.Expense, "✈️", "#03A9F4"),
        new("gifts", "Подарки", CategoryKind.Expense, "🎁", "#F06292"),
        new("pets", "Питомцы", CategoryKind.Expense, "🐾", "#A1887F"),
        new("taxes", "Налоги и госплатежи", CategoryKind.Expense, "🏛️", "#546E7A"),
        new("fees", "Комиссии банка", CategoryKind.Expense, "🏦", "#9E9E9E"),
        new("loan_payment", "Платежи по кредитам", CategoryKind.Expense, "💳", "#B71C1C"),
        new("cash_withdrawal", "Снятие наличных", CategoryKind.Expense, "💵", "#33691E"),
        new("business_expense", "Расходы ИП", CategoryKind.Expense, "🧾", "#5D4037"),
        new("other_expense", "Прочие расходы", CategoryKind.Expense, "❔", "#BDBDBD"),

        // Доходы
        new("salary", "Зарплата", CategoryKind.Income, "💼", "#2E7D32"),
        new("business_income", "Доход ИП", CategoryKind.Income, "🏢", "#1B5E20"),
        new("interest", "Проценты по вкладам", CategoryKind.Income, "📈", "#00695C"),
        new("dividends", "Дивиденды и купоны", CategoryKind.Income, "💹", "#004D40"),
        new("cashback", "Кэшбэк и бонусы", CategoryKind.Income, "🎯", "#558B2F"),
        new("refund", "Возвраты", CategoryKind.Income, "↩️", "#689F38"),
        new("gift_income", "Подарки и переводы от людей", CategoryKind.Income, "🤝", "#7CB342"),
        new("other_income", "Прочие доходы", CategoryKind.Income, "❔", "#9CCC65"),

        // Технические
        new("transfer", "Перевод между своими счетами", CategoryKind.Transfer, "🔄", "#78909C"),
        new("investment", "Инвестиции (покупка/продажа)", CategoryKind.Investment, "📊", "#37474F"),
    ];

    /// <summary>MCC → код категории. Основные диапазоны.</summary>
    public static readonly IReadOnlyDictionary<string, string> MccToCode = BuildMcc();

    private static Dictionary<string, string> BuildMcc()
    {
        var d = new Dictionary<string, string>();
        void Range(int from, int to, string code) { for (var i = from; i <= to; i++) d[i.ToString("D4")] = code; }
        void One(string code, params int[] mccs) { foreach (var m in mccs) d[m.ToString("D4")] = code; }

        One("groceries", 5411, 5422, 5441, 5451, 5462, 5499, 5300, 5310, 5331);
        One("cafe", 5812, 5813, 5814);
        Range(4111, 4131, "transport"); One("transport", 4011, 4112, 4789);
        One("taxi", 4121);
        One("fuel", 5541, 5542, 5983);
        One("housing", 4900, 6513);
        One("telecom", 4812, 4814, 4816, 4899);
        Range(8011, 8099, "health"); One("health", 5912, 5975, 5976);
        Range(5611, 5699, "clothing"); One("clothing", 5941, 5948);
        Range(7829, 7841, "entertainment"); Range(7911, 7999, "entertainment"); One("entertainment", 5813, 7832, 7922, 7941);
        One("subscriptions", 4899, 5815, 5816, 5817, 5818, 7372);
        One("electronics", 5732, 5045, 5065, 5722, 5734);
        One("marketplaces", 5262, 5399, 5964, 5965, 5969, 5999);
        Range(8211, 8299, "education"); One("education", 5942);
        Range(3000, 3999, "travel"); One("travel", 4511, 4722, 7011, 7012);
        One("gifts", 5947, 5992);
        One("pets", 5995, 742);
        One("taxes", 9311, 9399, 9222, 9211);
        One("cash_withdrawal", 6010, 6011);
        One("fees", 4829, 6012, 6051);
        return d;
    }
}
