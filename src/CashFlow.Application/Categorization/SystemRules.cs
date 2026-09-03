using CashFlow.Domain.Ledger;

namespace CashFlow.Application.Categorization;

/// <summary>Системные правила по описанию/контрагенту для российских банков. Приоритет ниже пользовательских.</summary>
public static class SystemRules
{
    public sealed record Def(RuleField Field, RuleMatch Match, string Pattern, string CategoryCode, int Priority = 10);

    public static readonly IReadOnlyList<Def> All =
    [
        // Технические операции
        new(RuleField.Description, RuleMatch.Regex, @"комисси", "fees", 50),
        new(RuleField.Description, RuleMatch.Regex, @"плата за (обслуживание|уведомлен|смс)", "fees", 50),
        new(RuleField.Description, RuleMatch.Regex, @"(выдача|снятие) наличных|atm|банкомат", "cash_withdrawal", 50),
        new(RuleField.Description, RuleMatch.Regex, @"капитализац|процент(ы|ов)? (по|на) (вклад|остат)|выплата процентов", "interest", 50),
        new(RuleField.Description, RuleMatch.Regex, @"дивиденд|купон|погашение облигац|амортизац", "dividends", 50),
        new(RuleField.Description, RuleMatch.Regex, @"кэшбэк|кешбэк|cashback|бонус", "cashback", 50),
        new(RuleField.Description, RuleMatch.Regex, @"возврат|refund|отмена покупки", "refund", 45),
        new(RuleField.Description, RuleMatch.Regex, @"погашение кредит|платеж по кредит|ежемесячный платеж|минимальный платеж", "loan_payment", 50),
        new(RuleField.Description, RuleMatch.Regex, @"налог|уфк|казначейств|фнс|госпошлин|штраф гибдд|пени", "taxes", 45),
        new(RuleField.Description, RuleMatch.Regex, @"заработн|зарплат|аванс|оплата труда", "salary", 45),
        new(RuleField.Description, RuleMatch.Regex, @"оплата по (договору|счету|счёту)|за (оказанные )?услуги|акт выполненных", "business_income", 30),

        // Мерчанты (нормализованные подстроки)
        new(RuleField.Description, RuleMatch.Regex, @"ларек|киоск|магнит|лента|ашан|вкусвилл|дикси|метро|окей|азбука вкуса|самокат|яндекс лавка|купер|сбермаркет", "groceries", 20),
        new(RuleField.Description, RuleMatch.Regex, @"яндекс такси|yandex taxi|yandex go|ситимобил|uber|максим", "taxi", 20),
        new(RuleField.Description, RuleMatch.Regex, @"лукойл|роснефть|газпромнефть|татнефть|shell|башнефть|азс", "fuel", 20),
        new(RuleField.Description, RuleMatch.Regex, @"ozon|wildberries|вайлдберриз|aliexpress|яндекс маркет|мегамаркет", "marketplaces", 20),
        new(RuleField.Description, RuleMatch.Regex, @"мтс|билайн|мегафон|теле2|tele2|ростелеком|yota|дом\.ру", "telecom", 20),
        new(RuleField.Description, RuleMatch.Regex, @"жку|жкх|мосэнергосбыт|водоканал|тепло|управляющая компания|капремонт|квартплата", "housing", 20),
        new(RuleField.Description, RuleMatch.Regex, @"аптека|apteka|ригла|36,6|горздрав|клиник|стоматолог|медицин|инвитро|гемотест", "health", 20),
        new(RuleField.Description, RuleMatch.Regex, @"кинопоиск|яндекс плюс|okko|иви|ivi|spotify|apple\.com/bill|netflix|youtube|vk музыка|литрес|букмейт|telegram premium", "subscriptions", 25),
        new(RuleField.Description, RuleMatch.Regex, @"steam|playstation|xbox|nintendo|кино|cinema|театр|концерт|kassir|яндекс афиша", "entertainment", 20),
        new(RuleField.Description, RuleMatch.Regex, @"вкусно и точка|kfc|ростикс|burger king|бургер кинг|додо|dodo|шоколадница|кофемания|coffee|кофе|ресторан|кафе|бар |столовая|теремок", "cafe", 20),
        new(RuleField.Description, RuleMatch.Regex, @"dns|м\.видео|мвидео|эльдорадо|ситилинк|re:store|apple|samsung", "electronics", 20),
        new(RuleField.Description, RuleMatch.Regex, @"аэрофлот|s7|победа|ржд|rzd|туту|aviasales|booking|островок|отель|hotel|hostel", "travel", 20),
        new(RuleField.Description, RuleMatch.Regex, @"zara|h&m|uniqlo|спортмастер|lamoda|ламода|befree|gloria jeans|ostin|o'stin", "clothing", 20),
        new(RuleField.Description, RuleMatch.Regex, @"метро|мосметро|тройка|подорожник|автобус|троллейбус|трамвай|электричк|мцд|парковк", "transport", 15),
        new(RuleField.Description, RuleMatch.Regex, @"зоомагазин|четыре лапы|бетховен|petshop|ветеринар", "pets", 20),
        new(RuleField.Description, RuleMatch.Regex, @"skillbox|нетология|яндекс практикум|coursera|udemy|stepik|учебн|курс", "education", 15),
    ];
}
