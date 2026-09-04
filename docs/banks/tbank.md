# Т-Банк (бывш. Тинькофф)

Три легальных канала: **T-Invest API** (физлица, инвестиции) + его же **официальный MCP**, и **T-API / T-Business** (ИП/ЮЛ, расчётные счета). Для карт физлица официального API **нет** — только выписки (или серые каналы в конце статьи).

## 1. T-Invest API — инвестиции физлиц (брокерский счёт, ИИС)

- Документация: <https://developer.tbank.ru/invest/api>
- Прото-описание и SDK: <https://github.com/Tinkoff/investAPI> (gRPC proto; есть официальные SDK: .NET `Tinkoff.InvestApi` на NuGet, Python, Java, Go)
- Базовые адреса:
  - gRPC: `invest-public-api.tbank.ru:443`
  - REST: `https://invest-public-api.tbank.ru/rest` (POST, JSON)
  - Песочница: `sand-invest-public-api.tbank.ru` (токен отдельный)
- Аутентификация: Bearer-токен, генерируется в ЛК инвестора (Настройки → Доступ к API). Бывает **read-only** и с правом торговли — нам нужен только read-only.
- Основные методы (gRPC/REST, v1):
  - `GetAccounts` — счета пользователя (тип: брокерский/ИИС, статус)
  - `GetPortfolio` — позиции и оценка счёта → наш `ExternalPosition`
  - `GetOperations` / `GetWithdrawCashDocumentTypes` — операции по счёту (поступления дивидендов/купон, ввод/вывод денег) → `ExternalTransaction`
  - `GetInstrumentBy`, `InstrumentsService/*` — инструменты для обогащения
  - `MarketDataService/Candles, OrderBook` — котировки (не нужны для MVP)
- Маппинг в CashFlow: реализовано в `src/CashFlow.Connectors.TInvest/TInvestConnector.cs`.
- Практика: соединение бывает нестабильным → ретраи с экспоненциальной задержкой, кэш портфеля; на у обрезается лимит запросов в минуту (см. «тарифы» в доке — актуальные цифры в документации).

Пример REST-вызова (для отладки через curl):

```bash
curl -s https://invest-public-api.tbank.ru/rest/instruments-service/get-accounts \
  -H "Authorization: Bearer $TINVEST_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{}'
```

## 2. T-Invest MCP — официальный (проверено 2026-09-04)

- Документация: <https://developer.tbank.ru/invest/mcp>
- Эндпоинт: **`https://invest-public-api.tbank.ru/mcp`**
- Транспорт: Streamable HTTP (JSON-RPC), авторизация тем же Bearer-токеном Т-Инвестиций.
- Это **единственный официальный банковский MCP в РФ** на сегодня. Даёт агенту те же данные, что API: портфель, операции, счета, инструменты.

Подключение к любому MCP-клиенту (пример конфига):

```json
{
  "mcpServers": {
    "tinvest": {
      "type": "http",
      "url": "https://invest-public-api.tbank.ru/mcp",
      "headers": { "Authorization": "Bearer <read-only токен>" }
    }
  }
}
```

Как использовать в CashFlow (Blazor Server): не для синка, а для AI-фич — категоризатор/ассистент получает инструменты банка напрямую. Подключение из C# — в [mcp-for-app.md](mcp-for-app.md).

## 3. T-API (Т-Бизнес) — р/с ИП и ЮЛ

- Документация: <https://developer.tbank.ru/docs/api> (раздел «Счета и выписки»: <https://developer.tbank.ru/docs/api/scheta-i-vipiski>)
- База: `https://business.tbank.ru/openapi` (используется в `src/CashFlow.Connectors.TBank.Business`)
- Аутентификация: токен из ЛК Т-Бизнеса (Интеграции → T-API), заголовок `Authorization: Bearer <token>`. При выпуске токена выбираются права — запрашиваем **только «Счета и выписки»**.
- Ключевые эндпоинты:
  - `GET /api/v4/bank-accounts` — список расчетных счетов (номер, банк, валюта, статус, реквизиты)
  - `GET /api/v1/statement?accountId=&dateFrom=&dateTo=` — выписка по счёту (проводки, контрагенты, назначение) → `ExternalTransaction`
  - (не используем, read-only: `payments/*`, `deposits/*`, `clients/*`)
- Ограничения: у API есть суточные квоты на количество запросов (считаются на стороне банка, детали — в доке «Тарифы»).

Пример:

```bash
curl -s "https://business.tbank.ru/openapi/api/v4/bank-accounts" \
  -H "Authorization: Bearer $TBUSINESS_TOKEN"
```

## 4. Выписка физлица (карты/счета) — файлами

Официального API карт физлица нет. Легальный канал — выгрузка из приложения:
- Т-Банк: выгрузка операций в **XLSX/CSV** из «Истории» → парсер `src/CashFlow.Connectors.Statements/TBankOperationsParser.cs`.
- Дедуп по хэшу операции уже в `StatementImportService`.

## 5. ⚠️ Непризнанные/неофициальные MCP (не для продакшена)

| Проект | Что обёртывает | Риск |
|---|---|---|
| [icyberdeveloper/tbank-mcp](https://github.com/icyberdeveloper/tbank-mcp) (⭐25, активен) | **реверс мобильного API** `*.t-bank-app.ru`: счета, карты, операции, переводы, оплата счетов ~90 инструментов | нарушение условий банка, блокировка; логин/пароль проходят через сторонний софт |
| [Sprytin/tinkoff-investments-mcp-server](https://github.com/Sprytin/tinkoff-investments-mcp-server) (⭐12) | официальный T-Invest API (Python) | сам API легален, но проще официальный MCP (см. §2) |
| [pvragov/tinvest-mcp](https://github.com/pvragov/tinvest-mcp), [Tlmonko/tbank-mcp](https://github.com/Tlmonko/tbank-mcp) | обёртки T-Invest API | то же |
| [INMozel/tinkoff-trader-mcp](https://github.com/INMozel/tinkoff-trader-mcp) | T-Invest + **исполнение сделок** | противоречит политике read-only CashFlow — не использовать |

Позиция CashFlow: в коннекторы приложения реверс-мобильный API **не заводим** (секреты пользователя + риск блокировки аккаунта). Для локальных экспериментов пользователя — на его страх и риск, вне приложения.
