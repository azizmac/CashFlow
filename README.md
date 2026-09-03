# CashFlow AI

Self-hosted приложение для анализа личных и предпринимательских финансов: куда уходят деньги, кто прислал, кому ушло. Только чтение — никаких платежей и переводов.

## Что делает

- Собирает операции из выписок (Сбер PDF, Т-Банк XLSX) и API (T-Invest, T-API, Sber API) в **свою** базу данных.
- Распознаёт контрагентов (ИНН, счёт, телефон СБП, имя) и переводы между своими счетами.
- Категоризирует операции правилами + MCC; пользователь исправляет, правила учатся.
- Показывает картину по профилям (физлицо / ИП) и сводно.

## Принципы

- **Read-only**: ни один коннектор не умеет писать в банк. Токены запрашиваются с минимальными правами.
- **Local-first**: бэкенд и Postgres ставятся на компьютер пользователя (`docker compose up`). Данные никуда не уходят.
- **Шифрование**: секреты и персональные поля (ИНН, счета, телефоны) шифруются на уровне колонок; ключ выводится из мастер-пароля и в БД не хранится.

## Стек

.NET 9 · Blazor Server · EF Core + PostgreSQL · Docker

## Структура

```
src/
  CashFlow.Domain                  доменная модель (Ledger, Connections, Investments, Products)
  CashFlow.Application             use-cases, нормализация, импорт
  CashFlow.Connectors.Abstractions IConnector, External* DTO
  CashFlow.Connectors.Statements   парсеры выписок
  CashFlow.Connectors.TInvest      T-Invest API (gRPC)
  CashFlow.Connectors.TBank.Business  T-API
  CashFlow.Connectors.Sber.Business   Sber API (mTLS + OAuth2)
  CashFlow.Infrastructure          EF Core, шифрование, секреты
  CashFlow.Web                     Blazor Server
tests/
docs/                              исследование API банков, доменная модель
```

## Статус

Ранняя разработка. См. `docs/domain-model.md`.

## Лицензия

MIT
