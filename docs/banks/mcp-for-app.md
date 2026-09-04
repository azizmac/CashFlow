# MCP в CashFlow: клиент к банкам + свой сервер над БД

Model Context Protocol (MCP) — открытый протокол «инструменты для LLM-агента» (JSON-RPC поверх stdio или Streamable HTTP). В контексте CashFlow у MCP две роли:

1. **Мы — MCP-клиент**: AI-агент приложения (будущий ИИ-категоризатор, ассистент «где мои деньги») ходит в **официальный T-Invest MCP** за данными портфеля.
2. **Мы — MCP-сервер**: отдаём нормализованные данные CashFlow (счета, операции, контрагенты, категории) внешним агентам (Claude Desktop, Copilot, GigaChat). **Не проксируя банки** — наружу идёт только наша БД, уже очищенная и дедуплицированная.

## Клиент: C# SDK

- Официальный C# SDK (поддерживается сообществом MCP + Microsoft): <https://github.com/modelcontextprotocol/csharp-sdk>, NuGet `ModelContextProtocol`.
- Схема подключения к T-Invest MCP (проверено, что сервер живой и требует Bearer; имена методов сверяйте с версией SDK):

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://invest-public-api.tbank.ru/mcp"),
    Name = "tinvest",
});
// Bearer-токен берём из ISecretStore конкретного подключения, НЕ из appsettings.
// Заголовок подставляется через HttpMessageHandler/AdditionalHeaders — см. README SDK.

var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();          // каталог инструментов MCP
var result = await tools.First(t => t.Name == "GetPortfolio")
    .CallAsync(new Dictionary<string, object> { ["accountId"] = id });
// → маппить result в ExternalPosition[] и отдавать агенту
```

Когда что использовать:

| Задача | Канал |
|---|---|
| Фоновый синк счетов/операций | прямой API/gRPC-SDK (`src/CashFlow.Connectors.*`) |
| Агенту нужны инструменты банка «из коробки» | официальный T-Invest MCP |
| У пользователя не-Т брокер | прямой API брокера; MCP-обёртки — только если SDK нет |

## Сервер: свой MCP над БД CashFlow

Модель: `GET /questions → agent → our MCP tools → our DB`. Инструменты (все read-only):

- `get_accounts(userId?)` — счета, остатки, «мои» vs внешние
- `search_transactions(period, amountRange, text, category?, counterparty?)`
- `get_counterparties(query?)` — кто присылал/кому уходило, суммы за период
- `get_cashflow_summary(period, profile: person|ip)` — доходы/расходы/переводы между своими
- `get_categories()` + `get_uncategorized(limit)` — кандидаты на категоризацию (для ИИ-этапа)
- `propose_category(txId, category)` — **не** пишет сама: возвращает предложение, применяет пользователь (в стиле «ручная категория не перетирается»)

Реализация в Blazor Server: тот же `ModelContextProtocol` SDK умеет поднимать server; транспорт — Streamable HTTP на отдельном endpoint с собственной авторизацией (scoped API-key пользователя, выдаётся в ЛК → шифруется в `ISecretStore`). Пользователь сам решает, подключать ли к нему Claude/Copilot.

## Безопасность (обязательные правила)

1. MCP-сервер CashFlow обслуживает **одного пользователя на ключ**; никакого `list_all_users`.
2. Секреты банков наружу через MCP не отдаём никогда; инструменты — только над нормализованной моделью.
3. Никаких инструментов записи в сторону банков (нет `pay`, `transfer`, `place_order`) — совпадает с `IConnector`.
4. Все банковские MCP-эндпоинты — только Bearer read-only токены; при генерации токена в ЛК банка всегда выбирать минимальные права.
5. Непризнанные MCP (tbank-mcp, sber-mcp от третьих лиц) в инфраструктуру приложения не включать — см. предупреждения в разделах «неофициальные MCP» в [tbank.md](tbank.md) и [sber.md](sber.md).

## Быстрая проверка живости официального T-Invest MCP (curl)

```bash
curl -s -X POST https://invest-public-api.tbank.ru/mcp \
  -H "Authorization: Bearer $TINVE..._ID \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

(Формат ответа — SSE-поток JSON-RPC; при неверном токене — 401/403.)
