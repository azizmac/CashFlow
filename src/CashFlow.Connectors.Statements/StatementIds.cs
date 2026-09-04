using System.Globalization;

namespace CashFlow.Connectors.Statements;

/// <summary>
/// Внешние идентификаторы операций, одинаковые для разных форматов одной и той же выписки.
/// Транзакции дедуплицируются по ExternalId, поэтому XLSX, PDF и 1С одного р/с должны давать один ключ на документ.
/// </summary>
internal static class StatementIds
{
    /// <summary>Документ по р/с Сбера: дата проводки, номер документа, сумма со знаком, последние 4 цифры корр. счёта.</summary>
    public static string SberDocument(DateOnly date, string docNo, decimal signedAmount, string? corrAccount)
    {
        var last4 = corrAccount is { Length: >= 4 } a ? a[^4..] : "0000";
        var dir = signedAmount < 0 ? "D" : "C";
        return $"{date:yyyyMMdd}-{docNo.Trim().TrimStart('0').PadLeft(1, '0')}-{Math.Abs(signedAmount).ToString("0.00", CultureInfo.InvariantCulture)}-{dir}-{last4}";
    }
}
