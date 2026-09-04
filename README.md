# CashFlow AI

Self-hosted приложение для анализа личных и предпринимательских финансов: куда уходят деньги, кто прислал, кому ушло. **Только чтение** — никаких платежей и переводов.

## Что умеет (MVP)

- Импорт выписок: **Сбер** (PDF-выписка по карте), **Т-Банк** (выгрузка операций XLSX/CSV).
- API-подключения (read-only): **T-Invest API** (брокерские счета и ИИС), **T-API** (р/с ИП/ЮЛ в Т-Бизнесе), **Sber API** (р/с в СберБизнесе, mTLS + OAuth2).
- Контрагенты: автоматическое сопоставление по ИНН, счёту, телефону СБП, названию; свои счета распознаются как «Я».
- Переводы между своими счетами исключаются из доходов/расходов.
- Категоризация: правила (системные + свои), MCC, обучение на ручных исправлениях. Ручная категория никогда не перетирается.
- Профили **физлицо / ИП** у одного пользователя — раздельно и сводно.
- Многопользовательский: регистрация, вход, у каждого свои данные.

## Быстрый старт (Docker)

```bash
cp .env.example .env
# задать POSTGRES_PASSWORD и ENCRYPTION_MASTER_KEY (openssl rand -base64 32)
docker compose up -d --build
# http://localhost:8080 → Регистрация
```

Миграции и справочники применяются автоматически при старте.

## Разработка

Требуется .NET 9 SDK и PostgreSQL (локально или `docker compose up db`).

```bash
dotnet test
cd src/CashFlow.Web && dotnet run     # appsettings.Development.json содержит dev-ключ шифрования
```

Новая миграция: `cd src/CashFlow.Infrastructure && dotnet ef migrations add <Name> -o Persistence/Migrations`.

## Безопасность

- Все банковские API вызываются с вашего сервера; данные никуда не отправляются.
- Токены/сертификаты, ИНН, номера счетов, телефоны, сырые выписки шифруются **AES-256-GCM** на уровне колонок. Ключ — из `ENCRYPTION_MASTER_KEY` (env), в БД не хранится. Потеря ключа = потеря зашифрованных полей.
- Коннекторы не содержат методов записи (`IConnector` — только `Get*`). Токены запрашивайте у банка с минимальными правами: T-Invest — «только чтение», T-API — «счета и выписки», Sber API — только scope выписок.
- Пароли: ASP.NET Identity (PBKDF2), блокировка после 5 неудачных попыток.

## Структура

```
src/
  CashFlow.Domain                   сущности: Account, Transaction, Counterparty, Category, правила; сервисы матчинга
  CashFlow.Application              импорт (дедуп → контрагенты → переводы → категоризация), синк, сидинг
  CashFlow.Connectors.Abstractions  IConnector (read-only), IStatementParser, External* DTO
  CashFlow.Connectors.Statements    парсеры: SberPdfStatementParser, TBankOperationsParser
  CashFlow.Connectors.TInvest       gRPC, Tinkoff.InvestApi SDK
  CashFlow.Connectors.TBank.Business  REST /api/v4/bank-accounts, /api/v1/statement
  CashFlow.Connectors.Sber.Business   REST + mTLS + OAuth2 refresh_token (с ротацией)
  CashFlow.Infrastructure           EF Core + Npgsql, шифрование колонок, ISecretStore, миграции
  CashFlow.Web                      Blazor Server: обзор, операции, контрагенты, счета, импорт, подключения, категории, профили
tests/CashFlow.Domain.Tests         xUnit
docs/                               исследование API банков (bank-integrations.md, banks/ — карточки и MCP)
```

## Статус и ограничения

- Парсер Сбер PDF написан по типовой структуре выписки СберБанк Онлайн и требует проверки на реальных файлах (пишите issue с обезличенным примером).
- T-API и Sber API коннекторы реализованы по документации, без прогона на боевых учётках; JSON-поля читаются лениво с запасными именами.
- Open API ЦБ РФ (стандарт для физлиц, 2027+) — в планах, заготовка `ConnectorType.CbrOpenApi`.
- Локальный ИИ-категоризатор (Ollama) — следующий этап; сейчас правила + MCC.

## Лицензия

MIT
