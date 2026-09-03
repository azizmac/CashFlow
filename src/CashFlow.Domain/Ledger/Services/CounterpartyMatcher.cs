using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Ledger.Services;

public sealed record CounterpartyMatch(Counterparty Counterparty, bool Created, string Reason);

/// <summary>
/// Сопоставляет реквизиты из выписки с существующими контрагентами пользователя.
/// Порядок: ИНН → счёт → телефон → нормализованное имя → имя без ОПФ. Иначе создаёт нового.
/// </summary>
public sealed class CounterpartyMatcher
{
    private readonly string _userId;
    private readonly List<Counterparty> _known;
    private readonly HashSet<string> _ownAccounts;
    private readonly HashSet<string> _ownInns;
    private readonly HashSet<string> _ownPhones;

    public CounterpartyMatcher(string userId, IEnumerable<Counterparty> known, IEnumerable<string> ownAccountNumbers, IEnumerable<string> ownInns, IEnumerable<string> ownPhones)
    {
        _userId = userId;
        _known = known.ToList();
        _ownAccounts = ownAccountNumbers.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToHashSet();
        _ownInns = ownInns.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToHashSet();
        _ownPhones = ownPhones.Select(Counterparty.NormalizePhone).Where(p => p.Length > 0).ToHashSet();
    }

    public CounterpartyMatch? Resolve(CounterpartyRaw raw)
    {
        if (raw.IsEmpty) return null;

        // Свои реквизиты → Self
        if ((raw.Account is not null && _ownAccounts.Contains(raw.Account.Trim()))
            || (raw.Inn is not null && _ownInns.Contains(raw.Inn.Trim()))
            || (raw.Phone is not null && _ownPhones.Contains(Counterparty.NormalizePhone(raw.Phone))))
        {
            var self = _known.FirstOrDefault(c => c.Kind == CounterpartyKind.Self);
            if (self is null)
            {
                self = new Counterparty(_userId, "Я (свои счета)", CounterpartyKind.Self);
                _known.Add(self);
                Enrich(self, raw);
                return new CounterpartyMatch(self, true, "self");
            }
            Enrich(self, raw);
            return new CounterpartyMatch(self, false, "self");
        }

        Counterparty? found = null;
        string reason = "";

        if (raw.Inn is { Length: 10 or 12 } inn)
        {
            found = _known.FirstOrDefault(c => c.Inn == inn);
            reason = "inn";
        }
        if (found is null && raw.Account is { Length: > 0 } acc)
        {
            found = _known.FirstOrDefault(c => c.Accounts.Contains(acc.Trim()));
            reason = "account";
        }
        if (found is null && raw.Phone is { Length: > 0 } ph)
        {
            var p = Counterparty.NormalizePhone(ph);
            if (p.Length > 0) { found = _known.FirstOrDefault(c => c.Phones.Contains(p)); reason = "phone"; }
        }
        if (found is null && !string.IsNullOrWhiteSpace(raw.Name))
        {
            var n = TextNormalizer.Normalize(raw.Name);
            found = _known.FirstOrDefault(c => c.Aliases.Contains(n));
            reason = "alias";
            if (found is null)
            {
                var stripped = TextNormalizer.StripLegalForm(n);
                if (stripped.Length >= 4)
                {
                    found = _known.FirstOrDefault(c => c.Aliases.Any(a => TextNormalizer.StripLegalForm(a) == stripped));
                    reason = "name";
                }
            }
        }

        if (found is not null)
        {
            Enrich(found, raw);
            return new CounterpartyMatch(found, false, reason);
        }

        var displayName = string.IsNullOrWhiteSpace(raw.Name)
            ? raw.Phone is not null ? $"Телефон {Counterparty.NormalizePhone(raw.Phone)}"
            : raw.Account is not null ? $"Счёт …{raw.Account[^Math.Min(4, raw.Account.Length)..]}"
            : "Неизвестный контрагент"
            : raw.Name.Trim();

        var kind = GuessKind(raw);
        var created = new Counterparty(_userId, displayName, kind);
        Enrich(created, raw);
        _known.Add(created);
        return new CounterpartyMatch(created, true, "new");
    }

    private static void Enrich(Counterparty c, CounterpartyRaw raw)
    {
        if (!string.IsNullOrWhiteSpace(raw.Name)) c.AddAlias(raw.Name);
        if (raw.Account is { Length: > 0 }) c.AddAccount(raw.Account);
        if (raw.Phone is { Length: > 0 }) c.AddPhone(raw.Phone);
        if (c.Inn is null && raw.Inn is { Length: 10 or 12 }) c.SetInn(raw.Inn, raw.Kpp);
        if (c.Kind == CounterpartyKind.Unknown)
        {
            var k = GuessKind(raw);
            if (k != CounterpartyKind.Unknown) c.SetKind(k);
        }
    }

    public static CounterpartyKind GuessKind(CounterpartyRaw raw)
    {
        var n = TextNormalizer.Normalize(raw.Name);
        var hintIp = n.StartsWith("ип ") || n.Contains(" ип ") || n.Contains("индивидуальный предприниматель");
        var byInn = Counterparty.KindFromInn(raw.Inn, hintIp);
        if (byInn != CounterpartyKind.Unknown) return byInn;
        if (hintIp) return CounterpartyKind.SoleProprietor;
        if (n.Contains("ооо") || n.Contains("ао ") || n.StartsWith("ао") || n.Contains("пао") || n.Contains("зао")) return CounterpartyKind.Company;
        if (n.Contains("уфк") || n.Contains("фнс") || n.Contains("казначейств") || n.Contains("налог") || n.Contains("госуслуги") || n.Contains("гибдд")) return CounterpartyKind.Government;
        if (n.Contains("банк") || n.Contains("bank")) return CounterpartyKind.Bank;
        if (raw.Phone is { Length: > 0 }) return CounterpartyKind.Person;
        if (raw.Mcc() is not null) return CounterpartyKind.Merchant;
        return CounterpartyKind.Unknown;
    }
}

internal static class CounterpartyRawExtensions
{
    // Место для расширения: MCC не входит в CounterpartyRaw, но паттерн оставлен для будущего.
    public static string? Mcc(this CounterpartyRaw _) => null;
}
