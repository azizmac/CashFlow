# CashFlow AI — правила проекта для Claude

Файл читается в начале каждой сессии. Любая задача начинается со сверки с этим стеком и правилами.

## Стек технологий (tech tag)

| Слой | Технология |
|------|------------|
| Язык / платформа | C# 13, .NET 9 (SDK 9.0.3xx), nullable включён |
| Хранилище | PostgreSQL 16 (Npgsql, EF Core 9, миграции в `CashFlow.Infrastructure`), схема `cashflow` |
| Сервер | ASP.NET Core minimal API (`CashFlow.Api`), ASP.NET Identity (cookie для веба + bearer-токены `MapIdentityApi` для клиентов), composition root `CashFlow.Server` |
| Веб | Blazor Server (interactive server render mode), страницы из Razor Class Library `CashFlow.UI` |
| Приложение | .NET MAUI Blazor Hybrid (`CashFlow.Maui`): Windows/macOS — «общая сборка» со встроенным Kestrel-сервером (`Services/EmbeddedServer.cs`), Android/iOS — клиент к удалённому серверу |
| Клиент API | `CashFlow.Client` — HTTP-реализации контрактов Application, SecureStorage для сессии |
| Парсеры выписок | PdfPig (PDF), ClosedXML (XLSX), 1CClientBankExchange (txt), CSV — проект `CashFlow.Connectors.Statements` |
| Интеграции банков | OAuth (PKCE) коннекторы Сбер / Т-Банк / Альфа в `CashFlow.Connectors.*` |
| Безопасность | AES-GCM шифрование полей (`Encryption:MasterKey`), маскирование номеров и телефонов в DTO |
| UI-дизайн | Дизайн-система «сдержанное жидкое стекло» из `docs/design/cashflow-ai-ui-mockups/project/CashFlow AI.dc.html`; токены — CSS-переменные в `src/CashFlow.UI/wwwroot/app.css`; тёмная тема базовая, светлая через `[data-theme=light]` |
| Инфраструктура | Docker Compose (`web` на 8080, `db` PostgreSQL на `127.0.0.1:55432`), Docker MCP `tools/docker-mcp` |
| Тесты | xUnit: `tests/CashFlow.Domain.Tests` (домен, парсеры) и `tests/CashFlow.Api.Tests` (интеграционные, PostgreSQL на 55432, база на прогон) |
| Утилиты | `tools/StatementProbe` — прогон реальных выписок через парсеры и БД |

## Архитектурные правила

- Слои: Domain → Application (DTO-контракты, интерфейсы `IProfileService, ILedgerQueries, ILedgerCommands, ICategoryService, IImportService, IConnectionsService`) → Infrastructure / Connectors → Api → Server → хосты (Web, Maui).
- Наружу (UI, API, MAUI) уходят **только DTO** из `CashFlow.Application.Contracts`. Доменные сущности, `CredentialRef`, полные номера счетов и телефоны наружу не отдаются.
- Каждая команда получает `userId` явно и проверяет владельца. Веб берёт его из cookie, API — из токена, клиент MAUI передаёт e-mail, сервер подставляет свой.
- `CashFlow.UI` знает только контракты Application и две абстракции хоста: `ICurrentUser`, `IAppShell`. Никаких ссылок на Infrastructure и EF.
- Всё новое серверное подключение регистрируется один раз в `CashFlow.Server/ServerHosting.cs` — его используют и веб, и встроенный сервер приложения.
- Внешний ключ операций из выписок — `StatementIds.SberDocument(...)`, чтобы разные форматы одной выписки дедуплицировались.

## Чувствительные данные — запрещены в репозитории

Репозиторий публичный на GitHub. В код, тесты, документацию, макеты, скриншоты и коммиты нельзя вносить реальные выписки и их фрагменты, ФИО, ИНН/счета/карты/телефоны/e-mail людей и контрагентов из данных владельца, остатки и обороты реальных счетов, пароли, ключи (`ENCRYPTION_MASTER_KEY`), токены и сертификаты банков, `.env`, `server.json`. Примеры и тесты — только с вымышленными реквизитами (счета `4070281010000000xxxx`, ИНН `77000000xx`, «ООО "Ромашка"», «Иванов Иван Иванович», `+7 900 123-45-67`, `*@example.com`). Реальные файлы для проверки парсеров лежат в `docs/vendor/` (gitignored). Перед коммитом: `python tools/secret-scan.py --staged` (хук `.githooks/pre-commit`, подключается `git config core.hooksPath .githooks`; на GitHub — Actions `secret-scan`). Подробно — skill `.claude/skills/no-sensitive-data/SKILL.md`. Если реальные данные попали в историю — переписать её до пуша и сообщить владельцу.

## Конвенции

- Отвечать и комментировать код по-русски; идентификаторы и термины — как есть. Тексты ошибок для пользователя — по-русски (`RussianIdentityErrorDescriber`, переводы в `ApiClient`).
- Перед изменением UI сверяться с макетом `.dc.html` и токенами `app.css`; новые экраны рисовать в той же системе (glass-карточки radius 24, акцент `#2f6bff`, eyebrow-лейблы, tabular-nums).
- Для UI и анимаций обязательны скиллы в `.claude/skills` (репозиторий emilkowalski/skills, «Apple-дизайн»): `apple-design` (материалы, пружины, жесты, типографика, reduced motion), `emil-design-eng` (решения по анимации, отклик на нажатие, кривые), `animate`, `review-animations`, `improve-animations`, `find-animation-opportunities`, `animation-vocabulary`, `prototype`, `pick-ui-library`. Токены движения уже заданы в `app.css` (`--ease-out`, `--ease-in-out`, `--ease-drawer`, `--t-press/--t-fast/--t-ui`). Для дизайн-задач также плагин `design` (`design:design-system`, `design:design-handoff`, `design:design-critique`) и skill `design` (Claude Design canvas).
- План работ и статусы задач — `docs/ROADMAP.md` (ссылаться на номера R-xx, обновлять статус после выполнения).
- После правок: `dotnet build` затронутых хостов, `dotnet test tests/CashFlow.Domain.Tests`, при изменении сервера — `docker compose up -d --build web`, при изменении MAUI — сборка `-f net9.0-windows10.0.19041.0`.
- Патчи файлов с обратными слэшами делать инструментами Edit/Write, а не python-скриптом через Bash heredoc (Bash разэкранирует `\\`).
- Порт 5432 на машине разработчика занят локальным PostgreSQL; контейнерная база проброшена на 55432. Настройки настольного приложения: `%LOCALAPPDATA%\User Name\ru.cashflow.app\Data\server\server.json`, лог `server.log`.
- Реальные выписки для проверки парсеров лежат в `docs/vendor/` (не коммитятся); прогонять `tools/StatementProbe`.
- Коммитить только по просьбе пользователя. Сообщения коммитов — по-русски.
