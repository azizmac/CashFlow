# Сбер

Статус-кво (2026-09): **официального API для физлиц (карты/счета СберБанк Онлайн) нет**. Официальный API — СберБизнес (р/с ИП/ЮЛ) и инвестиции только через терминалы/отчёты. Официального **банковского MCP у Сбера нет** — есть сторонние обёртки (одна легальная над бизнес-API, вторая — реверс СберБанк Онлайн, ⚠️).

## 1. СберБизнес API — р/с ИП/ЮЛ (официально)

- Портал: <https://developers.sber.ru/portal/products/sber-api>, обзор: <https://developers.sber.ru/docs/ru/sber-api/overview>
- Доступ к выпискам р/с: через СберБизнес (заявка на подключение API, выпуск сертификата `.p12/.pfx`, регистрация клиента в ЛК).
- Аутентификация (реализовано в `src/CashFlow.Connectors.Sber.Business/SberBusinessConnector.cs`):
  - **mTLS** (сертификат клиента) + **OAuth2**: обмен `refresh_token` → `access_token`; Сбер ротирует refresh-токен — обязательна пересылка нового секрета в `ISecretStore` через `ConnectionContext.OnSecretsRotated` (в коннекторе это есть).
  - Токен-эндпоинт (корпоративный SSO): `https://fintech.sberbank.ru:9443/ic/sso/api/v2/oauth/token`
  - Вариант публичного OAuth-шлюза (используется сторонними обёртками): `https://api.sberbank.ru:8443/prod/tokens/v2/oauth` + клиентские `client_id/secret` (client_credentials). ⚠️ какой именно путь вам выдаст банк — зависит от типа подключения, уточняйте при выпуске сертификата.
- API-база: `https://fintech.sberbank.ru:9443/fintech/api/`
- Scope'ы (минимальные для CashFlow): `GET_CLIENT_ACCOUNTS` (счета), `GET_STATEMENT_ACCOUNT` (выписка). **Не запрашивать** платёжные scope'ы (`PAY_DOC_RU` и т.п.) — приложение только читает.
- Основные операции: список расчетных счетов → баланс/реквизиты (`ExternalAccount`); выписка по счёту за период → проводки с контрагентами (`ExternalTransaction`). Точные пути методов — в документации конкретного тарифа доступа (они отличаются у «Sber API» и корпоративного шлюза).

Проверка токена (отладка):

```bash
curl -s --cert client.p12:PASS --cert-type P12 \
  "https://fintech.sberbank.ru:9443/fintech/api/...accounts" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

## 2. Выписка физлица — PDF

Основной легальный канал для карт/счетов: выписка из СберБанк Онлайн (PDF) → `src/CashFlow.Connectors.Statements/SberPdfStatementParser.cs`. Парсер написан по типовой структуре — дорабатывается по реальным файлам (issue с обезличенным примером).

## 3. MCP-обёртки

### 3.1 `theYahia/sber-mcp` — над официальным СберБизнес API (относительно чисто)

- npm: [`@theyahia/sber-mcp`](https://www.npmjs.com/package/@theyahia/sber-mcp), исходники: <https://github.com/theYahia/sber-mcp> (MIT, часть серии [WWmcp](https://github.com/theYahia/WWmcp)), CI активен (последний коммит 2026-09-03).
- 8 инструментов: счета, баланс, выписки, платежи, контрагенты. Транспорт: stdio или Streamable HTTP (`--http`, эндпоинт `POST /mcp`, есть `GET /health`).
- Конфиг (наш сценарий — **read-only**, токен без платёжных scope):

```json
{
  "mcpServers": {
    "sber": {
      "command": "npx",
      "args": ["-y", "@theyahia/sber-mcp"],
      "env": {
        "SBER_TOKEN": "<bearer из СберБизнес API>",
        "SBER_BASE_URL": "https://fintech.sberbank.ru:9443",
        "SBER_OAUTH_URL": "https://api.sberbank.ru:8443/prod/tokens/v2/oauth",
        "SBER_OAUTH_SCOPE": "GET_CLIENT_ACCOUNTS GET_STATEMENT_ACCOUNT",
        "SBER_PFX_PATH": "/secure/client.p12",
        "SBER_PFX_PASSPHRASE": "…только в локальном конфиге агента…"
      }
    }
  }
}
```

- Инструменты платежей в агент не отдаём (scope + whitelist инструментов) — политика `IConnector` read-only.

### 3.2 ⚠️ `ex3lite/sber-mcp` — реверс СберБанк Онлайн (физлица)

- PyPI: `sber-unofficial-mcp` (+ SDK `sber-unofficial-sdk`), <https://github.com/ex3lite/sber-mcp> (⭐4, активен, CI, есть `--demo` без банка).
- Читает продукты/операции **личного** СберБанк Онлайн, browserless-вход (логин/пароль/SMS/PIN локально, профиль `0600`), по умолчанию только чтение, переводы за флагом `--allow-writes` с prepare/confirm.
- Позиция CashFlow: **в приложение не интегрировать** — нарушает условия использования сервиса, есть риск блокировки клиента, и логин/пароль от банка в нашей модели не хранятся никогда. Упоминаем как вариант для локальных экспериментов самого пользователя.

### 3.3 AI-стек Сбера (не банкинг)

`theYahia/gigachat-mcp` — обёртка GigaChat (LLM Сбера) на случай, если агент приложения хочет работать через отечественную модель. Это не источник финансовых данных.

## 4. Сбер Инвестиции

Публичного API нет (портфель — только отчёты/выписки или терминал). Если у пользователя брокерский счёт в «Сбер Инвестициях» — канал: импорт выписки/спред отчёта. (Брокеры с API — в [brokers.md](brokers.md).)

## 5. Будущее: Open API ЦБ

Счета/карты физлица в СБЕРБанке должны стать доступны по стандарту Открытых API ЦБ — см. [cbr-open-api.md](cbr-open-api.md). Тогда появится коннектор `CbrOpenApi` вместо файлоимпорта.
