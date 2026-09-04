# Банки: API и MCP — справочник для интеграции

Практический справочник по каналам данных для CashFlow AI. Общее исследование — [../bank-integrations.md](../bank-integrations.md), здесь — конкретика: эндпоинты, аутентификация, scope'ы, MCP-серверы, примеры.

Проверено 2026-09-04: официальные доки developer.tbank.ru (включая живой MCP-эндпоинт), README живых MCP-репозиториев (GitHub/PyPI/npm), эндпоинты из кода коннекторов этого репозитория. Всё остальное помечено ⚠️ или «уточнить».

## Файлы

| Файл | Что внутри |
|---|---|
| [tbank.md](tbank.md) | Т-Банк: T-Invest API + официальный MCP, T-API (ИП/ЮЛ), неофициальные MCP для физлиц |
| [sber.md](sber.md) | Сбер: СберБизнес API (mTLS+OAuth2), импорт PDF-выписки, MCP-обёртки (бизнес и физики) |
| [other-banks.md](other-banks.md) | Альфа, ВТБ, остальные банки РФ — что есть, чего нет |
| [brokers.md](brokers.md) | Брокеры: Finam (+finam-mcp), АЛОР, БКС |
| [cbr-open-api.md](cbr-open-api.md) | Открытые API ЦБ РФ: стандарт, эндпоинты, сроки, заготовка коннектора |
| [mcp-for-app.md](mcp-for-app.md) | Как встроить MCP в .NET-приложение: C# SDK, клиент к T-Invest MCP, свой MCP-сервер поверх базы |

## Карта каналов (шпаргалка)

| Кто пользователь | Какие данные | Какой канал | Статус |
|---|---|---|---|
| Физлицо | брокерский счёт, ИИС | **T-Invest API** (gRPC/REST) или **официальный T-Invest MCP** | ✅ готово: `src/CashFlow.Connectors.TInvest` |
| Физлицо | карты/счета/кредиты | **импорт выписок** (Сбер PDF, Т-Банк XLSX/CSV); позже — Open API ЦБ | ✅ парсеры есть; ❌ официального API нет |
| ИП/ЮЛ | расчётный счёт | **T-API** (`business.tbank.ru/openapi`), **СберБизнес API**, **Alfa API** | ✅ коннекторы T-Business и Sber.Business есть |
| Физлицо | карты в «серой» зоне | неофициальные MCP (tbank-mcp, sber-mcp) — реверс мобильных API | ⚠️ риск блокировки, не для продакшена |
| Все | котировки/курсы | T-Invest market data, MOEX ISS, XML ЦБ | ✅ публично |

## Принципы интеграции (по этому репозиторию)

1. Только чтение: `IConnector` (см. `src/CashFlow.Connectors.Abstractions/Contracts.cs`) не имеет методов записи — так и остаётся, какие бы API/MCP мы ни подключали.
2. Каждый источник — коннектор с маппингом в `ExternalAccount / ExternalTransaction / ExternalPosition / ExternalProduct`.
3. Токен банка запрашиваем с минимальным scope: T-Invest — read-only токен из ЛК; T-API — «счета и выписки»; СберБизнес — только `GET_CLIENT_ACCOUNTS` + `GET_STATEMENT_ACCOUNT` (без платёжных scope).
4. Секреты — только в `ISecretStore` (AES-256-GCM), в git не попадают. Никогда не храним логин/пароль от интернет-банка.
5. MCP — способ дать доступ **AI-агенту** (категоризатор, ассистент). Для обычного фонового синка быстрее и проще прямой вызов API/SDK.

## Что добавить следующим

1. ~~T-Invest API~~, ~~T-API~~, ~~Sber Business~~ — готовы.
2. Коннектор **Alfa-Business API** (по образцу `TBankBusinessConnector`).
3. `openapi-cbr` — заготовка `ConnectorType.CbrOpenApi`, активировать при появлении песочниц банков (2027).
4. Внутренний **MCP-сервер над нормализованной БД** CashFlow (см. mcp-for-app.md) — чтобы внешний агент (Claude/Copilot/GigaChat) мог задавать вопросы по финансам без доступа к банкам напрямую.
