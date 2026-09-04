# CashFlow AI — источники данных российских банков (API / MCP)

Дата исследования: 2026-09-03. Дополнение 2026-09-04: практические карточки по каждому банку/брокеру и MCP-интеграция — в каталоге **[banks/](banks/README.md)**.

## 1. Главный вывод

Официального API для **физлиц** у Сбера и Т-Банка (счета/карты/кредиты/вклады) на сегодня **нет**. Официальные API — только для бизнеса (ИП/ЮЛ) и для инвестиций (Т-Инвестиции). Единый стандарт Открытых API ЦБ РФ для физлиц принят (введение с 01.10.2026), но реальный обмен данными ожидается не раньше 2027 г. после принятия федерального закона.

Значит, архитектура приложения должна быть **многоканальной**: официальные API там, где они есть, плюс импорт выписок и парсинг уведомлений как основной канал для физлиц, с готовностью подключить Open API ЦБ, когда он заработает.

## 2. Сводная таблица

| Источник | Для кого | Данные | Протокол / доступ | Статус |
|---|---|---|---|---|
| **T-Invest API** (Т-Инвестиции) | физлица (брокерский счёт, ИИС) | портфель, позиции, операции, купоны/дивиденды, котировки, налоги | gRPC + REST, токен из ЛК (read-only / full) | ✅ работает, стабильно документирован |
| **T-Invest MCP** (официальный) | физлица | то же через MCP-инструменты | `https://invest-public-api.tbank.ru/mcp`, HTTP, Bearer-токен | ✅ официальный, единственный официальный банковский MCP в РФ |
| **T-API** (Т-Бизнес) | ИП / ЮЛ | расчётные счета, выписки, депозиты, платежи, самозанятые, бизнес-карты | REST, токен в ЛК Т-Бизнеса | ✅ работает (у тебя есть ИП → можно подключить свой р/с) |
| **Sber API** (СберБизнес) | ИП / ЮЛ | счета, выписки, транзакции, депозиты, кредиты | OAuth2 + сертификаты, через СберБизнес | ✅ работает, для физлиц недоступен |
| **Alfa API** | ИП / ЮЛ | счета, выписки, платежи | REST/OAuth | ✅ бизнес-только |
| **Открытые API ЦБ РФ** (СТО БР, Ассоциация ФинТех) | физлица + ЮЛ | `/accounts`, `/balances`, `/transactions`, `/payment-cards`, `/products` (вклады/кредиты как productType) | FAPI / OpenID Connect, согласие клиента, scope `obru_accounts_pe` | ⏳ стандарт v2.0 введён с 01.10.2026, обязательные сроки перенесены — обмен ожидается в 2027 |
| **tbank-mcp** (неофициальный, icyberdeveloper) | физлица | счета, карты, операции, инвестиции — 90 инструментов | реверс мобильного API `*.t-bank-app.ru`, локально | ⚠️ нарушает условия банка, может отвалиться в любой момент |
| **tinkoff-investments-mcp-server** (Sprytin), **t-bank-invest-mcp-read-only** (sicness) | физлица | инвестиции | обёртки над T-Invest API | ✅ open source, но проще взять официальный MCP |
| **Finam Trade API**, **ALOR OpenAPI**, **БКС Trade API** | физлица (брокер) | портфель, операции | gRPC/REST/WS | ✅ если пользователь у этих брокеров |
| Сбер Инвестиции, ВТБ Инвестиции, Альфа-Инвестиции | физлица | — | публичного API нет (QUIK/терминалы) | ❌ только выписки файлом |
| **Импорт выписок** (PDF/XLSX/CSV/HTML) | физлица, любой банк | операции, остатки | парсеры под каждый банк (Сбер PDF, Т-Банк XLSX, ВТБ PDF, Совкомбанк HTML) | ✅ универсальный fallback |
| **Парсинг push / SMS** | физлица, Android | операции в реальном времени | NotificationListenerService; SMS требует платного пакета | ✅ Android; ❌ iOS (Apple не даёт доступ) |
| **Скрейпинг интернет-банка** (как Дзен-мани / CoinKeeper) | физлица | всё, что в ЛК | логин/пароль клиента | ⚠️ Сбер блокирует с 10.2022, нестабильно, риск блокировки клиента |

## 3. Как закрыть каждую потребность CashFlow AI

| Потребность | Сейчас | После запуска Open API ЦБ |
|---|---|---|
| Баланс карт/счетов физлица | push-парсинг (Android), импорт выписки, ручной ввод | `GET /balances`, `/payment-card-balances` |
| История транзакций | импорт выписок + push | `GET /transactions` с фильтром по датам |
| Инвестиции | T-Invest API/MCP, Finam, ALOR, БКС | те же брокерские API (Open API покрывает брокеров с 2026–27) |
| Кредиты, вклады | импорт выписки/скриншота, ручной ввод | `GET /products` (productType) |
| Расчётный счёт ИП | T-API, Sber API, Alfa API | то же |
| Курсы валют/ставки | API ЦБ РФ (`cbr.ru/scripts/XML_daily.asp`), MOEX ISS | — |

## 4. Рекомендуемая архитектура коннекторов

1. **Ядро** — единая модель `Account / Transaction / Position / Product`, к которой приводятся все источники (нормализация знаков, валют, дедупликация по хэшу операции, распознавание переводов между своими счетами).
2. **Коннекторы** как плагины с одинаковым интерфейсом `fetchAccounts / fetchTransactions / fetchPositions`:
   - `tinvest` — gRPC (официальный SDK есть для .NET, Python, Java, Go) — **первый к реализации**.
   - `tbusiness`, `sberbusiness`, `alfabusiness` — REST, для ИП/ЮЛ.
   - `statement-import` — парсеры файлов (PyMuPDF/pandas или .NET аналоги), поддержка Сбер/Т-Банк/ВТБ/Альфа/Совкомбанк.
   - `push-listener` — только Android, регэкспы по шаблонам уведомлений банков.
   - `openapi-cbr` — заготовка под стандарт СТО БР (FAPI/OIDC, согласия) — активировать в 2027.
3. **MCP-слой**: AI-агент приложения подключает официальный T-Invest MCP напрямую; для остального — свой внутренний MCP-сервер над нормализованной БД (не над банками).
4. **Безопасность**: токены только на устройстве или в зашифрованном хранилище; никакого хранения логина/пароля от интернет-банка (это ещё и нарушение договора с банком).

## 5. Ограничения и риски

- Open API ЦБ: сроки перенесены в 10.2025, законопроект не принят, тарифы за данные разрешены — планировать как «2027+».
- Неофициальные MCP/скрейперы — источник для MVP-экспериментов, не для продакшена.
- iOS: нет доступа к SMS/push → только импорт файлов и официальные API.
- T-Invest API отмечается как нестабильный по соединению → нужны ретраи и кэш.

## Источники

- [T-Invest MCP](https://developer.tbank.ru/invest/mcp), [T-Invest API](https://developer.tbank.ru/invest/api), [Tinkoff/investAPI](https://github.com/Tinkoff/investAPI)
- [T-API: счета и выписки](https://developer.tbank.ru/docs/api/scheta-i-vipiski), [T-API обзор](https://developer.tbank.ru/docs/api)
- [Sber API](https://developers.sber.ru/portal/products/sber-api), [Sber API overview](https://developers.sber.ru/docs/ru/sber-api/overview)
- [Alfa API](https://developers.alfabank.ru/products/alfa-api)
- [ЦБ РФ: Открытые API](https://www.cbr.ru/fintech/api/), [Стандарт получения информации о счетах, ОД-2890 от 19.12.2025](https://cbr.ru/Content/Document/File/185563/20251219_od_2890.pdf), [Методы для физлиц](https://wiki.opendatarussia.ru/specifications/accounts/v20/account-info-openapi-pe)
- [Итоги 2025 и перенос сроков Open API](https://rtln.ru/blog/otkrytye-api-banka-rossii-itogi-2025-goda-novye-sroki-obnovlenie-standartov/)
- [tbank-mcp (неофиц.)](https://glama.ai/mcp/servers/icyberdeveloper/tbank-mcp), [tinkoff-investments-mcp-server](https://github.com/Sprytin/tinkoff-investments-mcp-server), [t-bank-invest-mcp-read-only](https://lobehub.com/mcp/sicness-t-bank-invest-mcp-read-only)
- [Брокерские API в России 2025 (Хабр)](https://habr.com/ru/articles/963856/), [Finam Trade API](https://api.finam.ru/), [БКС Trade API](https://bcs.ru/trade-api)
- [Дзен-мани: подключение к Сберу](https://support.zenmoney.ru/knowledge-bases/2/articles/18-podklyuchenie-k-sber-banku-rossiya), [Дзен-мани: push](https://support.zenmoney.ru/knowledge-bases/2/articles/2144-sinhronizatsiya-pri-pomoschi-push-uvedomlenij-ot-bankov), [CoinKeeper импорт](https://about.coinkeeper.me/page24017104.html)
- [Агрегатор личных финансов (Хабр)](https://habr.com/ru/articles/784412/)
