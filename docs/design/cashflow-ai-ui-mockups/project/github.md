repo: azizmac/CashFlow
branch: main

## Last sync

date: 2026-09-04T16:20:00Z

### Updated in this project

- Прочитан README, доменная модель и Blazor-UI (обзор, операции, стили app.css)
- Созданы iPhone-first макеты CashFlow AI в стиле «жидкое стекло», тёмная и светлая темы
- Категории, контрагенты и метрики взяты из реального домена (SystemCategories, LedgerQueries)

## Screen map

| Экран в проекте | Файлы в репозитории |
|---|---|
| Обзор (1a, 1b, 1i) | src/CashFlow.Web/Components/Pages/Home.razor, Services/LedgerQueries.cs, wwwroot/app.css |
| Операции (1g) | src/CashFlow.Web/Components/Pages/Transactions.razor, Application/Categorization/SystemCategories.cs |
| Категории (1g) | src/CashFlow.Application/Categorization/SystemCategories.cs |
| Контрагенты и карточка (1c) | docs/domain-model.md (Counterparty, CounterpartyMatcher), Services/LedgerQueries.cs |
| Подключения и синк (1d) | docs/domain-model.md (Connection, SyncRun), src/CashFlow.Web/Services/ConnectionsFacade.cs |
| Импорт выписки (1d) | src/CashFlow.Application/Import/StatementImportService.cs, README.md |
| Настройки (1e) | docs/domain-model.md (§6a безопасность, FinancialProfile), README.md |
