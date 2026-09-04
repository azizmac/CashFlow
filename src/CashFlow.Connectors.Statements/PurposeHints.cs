using System.Text.RegularExpressions;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// Подсказки из назначения платежа и балансовых счетов Сбера: операции по бизнес-карте в выписке
/// приходят с контрагентом «Отделение ПАО Сбербанк» и счётом 30232/30233, а настоящий магазин — в назначении.
/// </summary>
internal static partial class PurposeHints
{
    [GeneratedRegex(@"^(?:Корректировка\.\s*)?(?<op>Покупка|Отмена покупки|Возврат покупки|Частичная отмена покупки|Снятие наличных)(?:\s+\w+_CB)?\s+в ТУ\s+(?:Сбербанка\s+)?(?<m>.+?)\s+по карте", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Purchase();

    /// <summary>Счёт незавершённых расчётов по картам — контрагент в выписке это банк, а не магазин.</summary>
    public static bool IsCardSettlementAccount(string? account) =>
        account is { Length: >= 5 } && (account.StartsWith("30232", StringComparison.Ordinal) || account.StartsWith("30233", StringComparison.Ordinal));

    /// <summary>Комиссии банка списываются на доходные счета 70601.</summary>
    public static bool IsBankIncomeAccount(string? account) =>
        account is { Length: >= 5 } && account.StartsWith("70601", StringComparison.Ordinal);

    /// <summary>Название торговой точки / суть операции из назначения платежа. null — ничего полезного.</summary>
    public static string? Merchant(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return null;
        var p = purpose.Trim();
        var m = Purchase().Match(p);
        if (m.Success) return m.Groups["m"].Value.Trim().TrimEnd('.');
        if (p.StartsWith("Зачисление средств по операциям эквайринга", StringComparison.OrdinalIgnoreCase)) return "Эквайринг Сбер";
        if (p.Contains("Приём ден. нал", StringComparison.OrdinalIgnoreCase) || p.Contains("Прием ден. нал", StringComparison.OrdinalIgnoreCase)) return "Внесение наличных";
        if (p.StartsWith("Выплата за операцию с бонусами", StringComparison.OrdinalIgnoreCase)) return "СберБизнес Спасибо";
        return null;
    }

    /// <summary>Что произошло по карте: покупка, отмена, возврат. Для описания операции.</summary>
    public static string? CardOperationKind(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return null;
        var m = Purchase().Match(purpose.Trim());
        return m.Success ? m.Groups["op"].Value : null;
    }
}
