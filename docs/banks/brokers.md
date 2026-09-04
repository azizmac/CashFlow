# Брокеры (кроме Т-Банка)

Нужно, если у пользователя брокерский счёт не в Т-Инвестициях. Единый паттерн: токен → счета/портфель/операции → `ExternalPosition/ExternalTransaction`. Никакого исполнения ордеров в CashFlow.

## Финам (Finam)

- **Trade API**: <https://api.finam.ru/> (REST + WebSocket, есть Postman-коллекция и демо-счёт). Аутентификация: логин/пароль кабинета → `loginPasswordToken`, далее `center/token` (API-ключ). Центры: `api.finam.ru`, `api.us.finam.ru` (для US-рынков).
- Методы для CashFlow: `GetMarketAccounts` (счета), `GetMarketCandles`/`GetMarketData` (оценки), сделки/поручения по счёту (`GetMarketOrders`, история сделок) — портфель целиком собирается из Noncapitalization/остатков, см. доки API.
- **MCP**: [FinamWeb/finam-mcp](https://github.com/FinamWeb/finam-mcp) — PyPI `finam-mcp-server`, Python 3.12+, FastMCP; stdio и HTTP (`http://localhost:3000/mcp` + заголовки с ключом). Инструменты: `get_account_info`, `get_transactions`, `get_trades`, `get_candles`, `get_quotes`, …
  - ⚠️ Сервер включает **торговые** инструменты (`place_order`, `cancel_order`, `cancel_all_orders`). Для CashFlow допустим только whitelist read-инструментов; в коннектор приложения — прямой Trade API.
- Python-обёртка API: `FinamTradeApiPy` (DBoyara/FinamTradeApiPy).

## АЛОР

- **ALOR OpenAPI**: REST + WebSocket, песочница и боевой контур (публичная документация — на `alor.dev`; при подключении уточнить актуальный портал у брокера ⚠️). Авторизация: токен из личного кабинета.
- Для нас: портфель (`portfolios/…`), сделки (`transactions/…`), котировки (`quotes/…`). SDK-генераторы: `brkly/alor_dev_auto_python`, `Ruvad39/go-alor`.
- MCP: официального нет (поиск 2026-09-04 пустой).

## БКС

- **БКС Trade API** (<https://bcs.ru/trade-api>): REST (портфель, сделки, котировки), ключ выдаётся в БКС-Модуле. MCP: нет.

## Обзор рынка

- Актуальный обзор брокерских API РФ: <https://habr.com/ru/articles/963856/>
- Сбер/ВТБ/Альфа-Инвестиции публичных API не имеют → только выписки файлом.

## Приоритет реализации

1. T-Invest (есть) — покрывает большинство инвесторов-физлиц.
2. Finam Trade API — есть официальная документация + MCP как референс.
3. АЛОР — по запросам.
4. БКС — по запросам.
