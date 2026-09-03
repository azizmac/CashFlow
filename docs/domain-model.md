# CashFlow AI — доменная модель и структура проекта (.NET)

Черновик от 2026-09-03. Стиль: модульный монолит, Clean Architecture, DDD-lite (агрегаты + value objects + доменные события), без лишних абстракций.

## 0. Принцип: только чтение

Приложение **не выполняет платежей, переводов и сделок** — ни одной операции, меняющей состояние в банке или у брокера. Это инструмент анализа: куда уходят деньги, разбивка по категориям (ИИ + человек), потоки доходов/расходов.

Технически закрепляется так:
- в `IConnector` нет методов записи; DTO только `External*` на чтение;
- токены запрашиваются с минимальными правами: T-Invest — **read-only** токен, Sber API / T-API — только права на выписки и остатки, Open API ЦБ — scope `obru_accounts_pe` (без платёжных scope);
- T-Invest MCP используется только инструментами чтения (портфель, операции); торговые инструменты не подключаются;
- собственный MCP-сервер `CashFlow.Mcp` отдаёт данные ИИ только на чтение + категоризацию, без доступа к коннекторам.

## 1. Ограниченные контексты (модули)

| Модуль | За что отвечает | Ключевые агрегаты |
|---|---|---|
| **Identity** | пользователь, профили (физлицо / ИП), устройства, шифрование секретов | `User`, `FinancialProfile`, `Device` |
| **Connections** | подключения к источникам, синхронизация, сырые данные | `Connection`, `SyncRun`, `RawRecord` |
| **Ledger** | счета, операции, контрагенты, категории, переводы между своими счетами | `Account`, `Transaction`, `Counterparty`, `Category`, `TransferLink` |
| **Products** | кредиты, вклады, кредитки — условия и графики | `Loan`, `Deposit`, `CreditCard` |
| **Investments** | инструменты, позиции, инвест-операции | `Instrument`, `Position`, `InvestmentOperation` |
| **Reference** | курсы валют (ЦБ), котировки (MOEX/T-Invest), справочник банков | `FxRate`, `Quote`, `Institution` |
| **CashFlow (Analytics)** | регулярные потоки, прогноз, бюджеты, AI-инсайты | `RecurringFlow`, `Forecast`, `Budget` |

Правило зависимостей: `Ledger`, `Products`, `Investments` не знают о `Connections`. Коннекторы пишут в них через команды приложения (`ImportTransactions`, `UpsertAccount`, `UpsertPosition`). `CashFlow` читает всё, ничего не пишет в другие модули.

## 2. Value objects (общее ядро)

```
Money            { decimal Amount; Currency Currency }   // никогда double, всегда с валютой
Currency         ISO-4217 (RUB, USD, …) + XAU/крипта при необходимости
DateRange        { DateOnly From, To }
ExternalRef      { ConnectorType Connector; string ExternalId }  // id объекта в источнике
DedupeKey        SHA-256(accountId, postedAt, amount, normalizedDescription, externalId?)
Percent          decimal с ограничением
```

## 3. Сущности по модулям

### Identity
- `User` — Id, Email, BaseCurrency (валюта отчётности), Settings.
- `FinancialProfile` — UserId, `Kind` (`Individual | SoleProprietor | Company`), Name, Inn?, Ogrn?. У одного пользователя несколько профилей (физлицо + ИП). Каждый `Account` принадлежит профилю → можно смотреть картину раздельно (личное / ИП) и **сводно** (реальная картина). Переводы «со своего р/с ИП на свою карту» — `TransferLink` между профилями, не доход.
- `Device` — для on-device коннекторов (push-listener), ключ шифрования локального хранилища.

### Connections
- `Institution` — Id, Name, Kind (`Bank | Broker | Wallet | Cash | Manual`), Bic/LicenseNo, Logo. Справочник: Сбер, Т-Банк, ЦБ-OpenAPI-провайдер…
- `Connection` (агрегат) — Id, UserId, InstitutionId, `ConnectorType` (`TInvest | TBankBusiness | SberBusiness | CbrOpenApi | StatementImport | PushListener | Manual`), `CredentialRef` (ссылка в секрет-хранилище, **не сам токен**), Status (`Active | NeedsReauth | Disabled | Error`), LastSyncAt, ConsentExpiresAt (для Open API).
- `SyncRun` — ConnectionId, StartedAt/FinishedAt, Result, ImportedCount, Errors. История синхронизаций.
- `RawRecord` — сырой JSON/строка выписки + DedupeKey. Хранится для повторной нормализации при смене парсера.
- `SyncSchedule` — ConnectionId, Interval (API: каждые N часов; инвестиции — 1 раз в день после закрытия торгов), `IncrementalFrom` (курсор: последняя дата/ID), RetryPolicy.
- `StatementInbox` — источник автоматического получения выписок там, где API нет (Сбер-физлицо): `WatchFolder` (папка на устройстве/ПК), `EmailInbox` (IMAP: банк присылает выписку на почту по расписанию), `ShareTarget` (поделиться PDF из приложения банка в CashFlow на телефоне). Файл → `StatementImportConnector` → парсер по банку → `RawRecord` → `Transaction`.

**Единая БД приложения — источник истины.** Всё, что получено из API или выписок, нормализуется и **сохраняется локально** (PostgreSQL на бэкенде, SQLite на устройстве с двусторонней синхронизацией). Банковские API вызываются только для дозагрузки инкремента; приложение полностью работает офлайн на своих данных. Из источников извлекаются и сохраняются: счёт, дата, сумма, валюта, контрагент (имя, ИНН, счёт, банк, телефон СБП), назначение платежа, MCC, статус, сырое описание.

### Ledger
- `Account` (агрегат) — Id, UserId, **ProfileId**, ConnectionId?, InstitutionId, `AccountType` (`Checking | Card | Savings | Deposit | CreditCard | Loan | Brokerage | Iis | Cash | EWallet`), Name, Currency, ExternalRef, IsArchived, IncludeInNetWorth, IncludeInCashFlow.
- `BalanceSnapshot` — AccountId, At, Current, Available, Blocked (`Money`). Пишется при каждой синхронизации → история чистых активов.
- `Transaction` (агрегат) — Id, AccountId, PostedAt, BookedAt?, Amount (`Money`, знак = направление), AmountInBase (`Money`, по курсу на дату), Status (`Pending | Posted | Cancelled`), Description, **CounterpartyId**, `CounterpartyRaw` { Name, Inn?, Kpp?, Account?, Bik?, BankName?, Phone? (СБП) } — как пришло из источника, Purpose (назначение платежа), Mcc?, CategoryId, Tags, Note, `Source` (ConnectorType), ExternalRef, DedupeKey, RawRecordId?, TransferLinkId?, `Kind` (`Regular | Transfer | Fee | Interest | LoanPayment | Investment | Fx | Cash | Tax`).
- `Counterparty` (агрегат) — Id, UserId, `DisplayName`, `Kind` (`Person | Company | SoleProprietor | Bank | Government | Merchant | Self`), Inn?, Kpp?, Ogrn?, Phones[], Accounts[] { Account, Bik }, `Aliases[]` (все варианты написания из выписок: «OZON», «ООО Ромашка Маркет», «OZON.RU»), DefaultCategoryId?, IsFavorite, Note. **Кто прислал / кому ушло** — всегда ссылка на эту сущность.
- `CounterpartyMatcher` (доменный сервис) — сопоставляет `CounterpartyRaw` с `Counterparty`: 1) по ИНН → 2) по номеру счёта/телефону СБП → 3) по нормализованному имени (без ООО/ИП/кавычек, регистра, транслита) → 4) по алиасам → иначе создаёт нового с `Kind`, определённым по ИНН (10 цифр — юрлицо, 12 — ИП/физлицо) или по паттерну описания. Свои собственные счета (в т.ч. р/с ИП ↔ карта физлица) → `Kind = Self` → автоматически `TransferLink`.
- `CounterpartyMerge` — пользователь может слить дубли; алиасы объединяются, транзакции перепривязываются.
- `TransferLink` — связывает две `Transaction` (списание + зачисление) между своими счетами → исключаются из доходов/расходов. Правило матчинга: сумма (с учётом курса), окно ±2 дня, противоположные знаки, разные счета.
- `Category` — дерево (ParentId), Name, Kind (`Income | Expense | Transfer | Investment`), IsSystem, Icon/Color. Системный набор + пользовательские.
- `CategorizationRule` — Pattern (regex/contains по описанию, MCC, ИНН контрагента), CategoryId, Priority, `Origin` (`System | User | AiLearned`).
- `Categorization` (на `Transaction`) — CategoryId, `Source` (`Rule | Mcc | Ai | User`), Confidence (0–1), ReviewedByUser (bool), ProposedCategoryId? (предложение ИИ, ждущее подтверждения).

Пайплайн категоризации (по убыванию приоритета):
1. **User** — ручная категория никогда не перетирается.
2. **Rule** — правила пользователя и правила, выученные из его ручных исправлений (`AiLearned`: контрагент X → категория Y после 2+ подтверждений).
3. **Mcc** — справочник MCC → категория.
4. **Ai** — LLM-классификатор по описанию/контрагенту/сумме; при Confidence < порога — категория остаётся `Uncategorized`, а ИИ-вариант кладётся в `ProposedCategoryId` для подтверждения одним тапом.
Каждое ручное исправление порождает событие `TransactionRecategorized` → генератор правил.

### Products
- `Loan` — AccountId (тип `Loan`), Principal, Rate, IssuedAt, MaturityAt, PaymentDay, `Schedule[]` { DueDate, Principal, Interest }, NextPayment. Платежи → `Transaction.Kind = LoanPayment`, из них выделяются проценты как расход.
- `CreditCard` — AccountId, CreditLimit, GraceDays, StatementDay, MinPayment, Debt.
- `Deposit` — AccountId, Rate, OpenedAt, MaturityAt, Capitalization (`None | Monthly | AtMaturity`), Replenishable, ExpectedInterest. Начисления → `Transaction.Kind = Interest` (пассивный доход).

### Investments
- `Instrument` — Id, Isin, Figi?, Ticker, Type (`Share | Bond | Etf | Currency | Futures`), Currency, LotSize, Issuer, MaturityAt/CouponRate для облигаций.
- `Position` — AccountId (Brokerage/Iis), InstrumentId, Quantity, AvgPrice, CurrentPrice, MarketValue, UnrealizedPnl, UpdatedAt.
- `InvestmentOperation` — AccountId, InstrumentId?, Type (`Buy | Sell | Dividend | Coupon | Amortization | Fee | Tax | Deposit | Withdrawal`), Quantity, Price, Amount (`Money`), At, ExternalRef. Денежные типы (Dividend, Coupon, Fee, Tax, Deposit, Withdrawal) **порождают `Transaction`** в Ledger на брокерском счёте — так инвестиции попадают в общий cash flow.

### Reference
- `FxRate` — Base, Quote, Date, Rate, Source (`Cbr | Moex`).
- `Quote` — InstrumentId, At, Price.

### Views (что видит пользователь — read-model, CQRS)
- **Лента операций** — дата, сумма, **контрагент** (кто прислал / кому ушло) с иконкой, категория, счёт, профиль (личное/ИП). Фильтр по любому полю.
- **Карточка контрагента** — все операции с ним, сумма за период, средний чек, тренд, «сколько всего ушло в X за год».
- **Потоки денег (Sankey)** — источники дохода → счета → категории/контрагенты расходов; переводы между своими счетами схлопнуты.
- **Топ куда уходит** — категории и контрагенты по сумме за период, сравнение с прошлым периодом.
- **Сводная картина** — чистые активы (счета + вклады + инвестиции − кредиты) по профилям и суммарно.

### CashFlow (Analytics)
- `RecurringFlow` — UserId, Name, Kind (`Income | Expense`), ExpectedAmount, Periodicity (cron-like / `Monthly(day)`), AccountId?, CategoryId?, MatchRule, Confidence, DetectedAutomatically. Это зарплата, аренда, подписки, купоны — "пассивные/активные" потоки.
- `Forecast` — вычисляемая проекция балансов на N месяцев из `RecurringFlow` + графиков `Loan`/`Deposit` + купонов по облигациям.
- `Budget` — CategoryId, Period, Limit.
- `Insight` — сгенерированный AI/правилами вывод, привязан к сущностям, с датой и статусом (прочитано/скрыто).

## 4. Доменные события

`TransactionImported`, `CounterpartyResolved`, `CounterpartyCreated`, `TransactionsCategorized`, `TransactionRecategorized`, `TransferLinked`, `StatementReceived`, `BalanceSnapshotTaken`, `PositionUpdated`, `SyncFailed`, `ConsentExpiring`, `RecurringFlowDetected`. Обработчики: авто-категоризация, матчинг переводов, пересчёт прогноза, уведомления.

## 5. Интерфейс коннектора

```csharp
public interface IConnector
{
    ConnectorType Type { get; }
    ConnectorCapabilities Capabilities { get; }   // Accounts | Balances | Transactions | Positions | Products | Realtime
    Task<IReadOnlyList<ExternalAccount>>      GetAccountsAsync(ConnectionContext ctx, CancellationToken ct);
    Task<IReadOnlyList<ExternalTransaction>>  GetTransactionsAsync(ConnectionContext ctx, string accountId, DateRange range, CancellationToken ct);
    Task<IReadOnlyList<ExternalPosition>>     GetPositionsAsync(ConnectionContext ctx, string accountId, CancellationToken ct);
    Task<IReadOnlyList<ExternalProduct>>      GetProductsAsync(ConnectionContext ctx, CancellationToken ct); // вклады/кредиты
}
```

`External*` — DTO коннектора, маппятся в домен в `Application` (единая точка нормализации знаков, валют, дат). Реализации:

| Коннектор | Где выполняется | Capabilities |
|---|---|---|
| `TInvestConnector` (gRPC, `Tinkoff.InvestApi`) | устройство | Accounts, Balances, Transactions (денежные операции), Positions |
| `TBankBusinessConnector` (REST) | устройство | Accounts, Balances, Transactions, Products (депозиты) |
| `SberBusinessConnector` (REST + mTLS + OAuth2) | устройство | Accounts, Balances, Transactions, Products |
| `CbrOpenApiConnector` (FAPI/OIDC) — заготовка | устройство (возможно, через наш прокси без хранения) | Accounts, Balances, Transactions, Products |
| `StatementImportConnector` (PDF/XLSX/CSV, парсер на банк) | устройство | Accounts, Transactions |
| `PushListenerConnector` | **только Android, на устройстве** | Transactions (Realtime) |
| `ManualConnector` | устройство | всё, руками |

## 6. Структура решения

```
CashFlow.sln
├─ src/
│  ├─ CashFlow.Domain/                  сущности, VO, события, интерфейсы репозиториев (без зависимостей)
│  │   ├─ Identity/  Connections/  Ledger/  Products/  Investments/  Reference/  CashFlow/
│  │   └─ Shared/   (Money, Currency, DateRange, Entity<T>, IDomainEvent)
│  ├─ CashFlow.Application/             use-cases (команды/запросы), маппинг External* → домен, политики дедупа
│  ├─ CashFlow.Connectors.Abstractions/ IConnector, External* DTO, ConnectorCapabilities
│  ├─ CashFlow.Connectors.TInvest/
│  ├─ CashFlow.Connectors.TBank.Business/
│  ├─ CashFlow.Connectors.Sber.Business/
│  ├─ CashFlow.Connectors.CbrOpenApi/
│  ├─ CashFlow.Connectors.Statements/   парсеры: Sber.Pdf, TBank.Xlsx, Vtb.Pdf …
│  ├─ CashFlow.Infrastructure/          EF Core + PostgreSQL (backend) / SQLite (устройство), секреты, планировщик синка
│  ├─ CashFlow.Security/                Argon2id, envelope-ключи, ISecretStore (Keychain/DPAPI/Keystore), SQLCipher-провайдер
│  ├─ CashFlow.Sync/                    E2E-шифрованные изменения, курсоры, разрешение конфликтов
│  ├─ CashFlow.Api/                     ASP.NET Core: Auth (2FA, устройства), хранилище blobs, публичные справочники. Не видит данных.
│  ├─ CashFlow.Mcp/                     собственный MCP-сервер над Ledger/CashFlow для AI-агента
│  ├─ CashFlow.Client.Core/             ViewModels (MVVM), офлайн-кэш, синхронизация с Api
│  ├─ CashFlow.Client.Avalonia/         общий UI (Desktop + Android + iOS + Browser)
│  └─ CashFlow.Client.Android.Push/     NotificationListenerService → PushListenerConnector
├─ tests/
│  ├─ CashFlow.Domain.Tests/            матчинг переводов, дедуп, Money
│  ├─ CashFlow.Connectors.Tests/        контрактные тесты на записанных ответах API, эталонные выписки
│  └─ CashFlow.Application.Tests/
└─ docs/
```

## 6a. Мультипользовательность и безопасность данных

Модель распространения: **приложение ставит любой человек на свой компьютер/телефон**, проходит авторизацию, подключает свои источники — и его данные принадлежат только ему. Мы (разработчики) не должны иметь технической возможности прочитать его ключи, пароли и операции.

### Архитектура: local-first + zero-knowledge sync

```
┌──────────── Устройство пользователя ────────────┐      ┌──── Наш сервер (опционально) ────┐
│ UI (Avalonia)                                    │      │ Auth (учётка, 2FA)               │
│ Application + Connectors  ← токены банков        │      │ Sync-хранилище: только blobs,     │
│ SQLite (SQLCipher, AES-256)  ← все данные        │─────▶│ зашифрованные на устройстве.     │
│ ISecretStore → Keychain / Keystore / DPAPI       │ E2E  │ Сервер НЕ имеет ключа.           │
│ Master key ← Argon2id(пароль) ⊕ ключ ОС          │      │ Реестр банков, курсы, MCC — публ.│
└──────────────────────────────────────────────────┘      └──────────────────────────────────┘
```

- **Данные и вызовы банковских API живут на устройстве.** Sber mTLS-сертификат, T-Invest токен, T-API токен — всё хранится в `ISecretStore` (macOS Keychain, Windows DPAPI/Credential Manager, Android Keystore, iOS Keychain, Linux libsecret) и никогда не отправляется на наш сервер.
- **База данных зашифрована целиком**: SQLite + SQLCipher (`Microsoft.Data.Sqlite` с SQLitePCLRaw.bundle_e_sqlcipher), AES-256-CBC/GCM постранично. Файл базы без ключа — шум.
- **Иерархия ключей (envelope encryption)**:
  - `MasterKey` = Argon2id(мастер-пароль пользователя, salt) — никуда не сохраняется в открытом виде;
  - `DeviceKey` — случайный ключ в OS-keystore, разблокирует базу без ввода пароля при биометрии/логине в ОС;
  - `DbKey`, `SecretsKey`, `SyncKey` — обёрнуты (`AES-KeyWrap`) `MasterKey`; ротация одного ключа не требует перешифрования всего.
  - Восстановление — recovery-код (24 слова, BIP39-подобный), показанный один раз при регистрации. Забыл пароль + потерял код = данные не восстановить. Это осознанная цена zero-knowledge, о ней предупреждаем.
- **Синхронизация между устройствами** (телефон ↔ ПК): бэкенд хранит только зашифрованные на клиенте blobs (`EncryptedChange { deviceId, seq, nonce, ciphertext, tag }`), ключ `SyncKey` есть только у устройств пользователя. Конфликты решаются на клиенте (CRDT-lite/last-write-wins по полям, редактируемым человеком: категория, заметка). Опционально пользователь может поднять **свой** sync-сервер (self-hosted Docker) или вовсе работать без сервера.
- **Авторизация в приложении** (модуль `Identity`): регистрация e-mail + пароль (или локальный аккаунт без сервера), TOTP-2FA, привязка устройств (новое устройство подтверждается со старого — передача `SyncKey` через QR / короткий код, как в Signal). Сессия — short-lived JWT + refresh в keystore.
- **Сервер знает минимум**: e-mail, хэш пароля (Argon2id), публичные ключи устройств, размер и даты blobs. Ни сумм, ни контрагентов, ни токенов банков. Утечка сервера не раскрывает финансов.
- **ИИ-категоризация без утечки**: приоритет — локальная модель (ONNX Runtime, малый классификатор на описание/MCC); при вызове внешней LLM отправляется **только обезличенное описание операции и сумма**, без ИНН, счетов, имён физлиц (маскирование в `Application` перед вызовом), и только с явного согласия пользователя в настройках.
- **Гигиена**: секреты никогда не пишутся в логи (`Redacted` типы для токенов), память с ключами — `SecureString`/zero-after-use, сборки подписаны, обновления через подписанный канал, зависимости на SBOM/Dependabot, рейт-лимит и блокировка после N неверных мастер-паролей.

### Влияние на модель

- `User` получает `AuthMethod` (`Local | Cloud`), `MasterKeySalt`, `KeyWrapVersion`.
- `Device` — `PublicKey`, `Name`, `LastSeenAt`, `TrustedAt`; отзыв устройства = ротация `SyncKey`.
- `Connection.CredentialRef` → ключ записи в `ISecretStore`, значение — `SecretsKey`-шифрованное.
- Новый модуль `Sync` (`EncryptedChange`, `SyncCursor`) в `Infrastructure`, домен о шифровании не знает.

## 7. Ключевые решения

1. **Коннекторы выполняются на устройстве пользователя** (local-first). Наш сервер банковские API не вызывает и токенов не хранит. Исключение в будущем — Open API ЦБ, если аккредитация третьей стороны потребует серверного mTLS; тогда только пропуск через сервер без хранения данных, с шифрованием ответа ключом устройства.
2. **Токены не покидают устройство.** `CredentialRef` в домене, сам секрет — в `ISecretStore` (Keychain/DPAPI/Keystore), см. раздел 6a.
3. **Сырые данные хранятся.** `RawRecord` позволяет перепрогнать нормализацию без повторного запроса к банку.
4. **Всё в базовой валюте считается лениво** по `FxRate` на дату операции; в `Transaction` кэшируется `AmountInBase`.
5. **Инвестиции — часть cash flow только через денежные операции.** Переоценка позиций влияет на чистые активы, но не на доходы/расходы, пока не зафиксирована.
6. **Транзакции неизменяемы от источника, изменяемы пользователем**: категория, теги, заметка, `TransferLink` — отдельные поля, которые синк не перетирает.
7. **Дедуп** по `DedupeKey` + `ExternalRef`; `Pending` → `Posted` обновляет, а не дублирует.

## 8. Что не делаем

**Никогда**: платежи, переводы, торговые операции — любые изменения состояния в банке/у брокера.

**Не в MVP**: бюджеты, мультипользовательские «семьи», скрейпинг интернет-банка.
