using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CashFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cashflow");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExternalConnector = table.Column<int>(type: "integer", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInNetWorth = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInCashFlow = table.Column<bool>(type: "boolean", nullable: false),
                    LastBalanceAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    LastBalanceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    LastBalanceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BalanceSnapshots",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    CurrentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AvailableAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    AvailableCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    BlockedAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    BlockedCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Connections",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CredentialRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncCursor = table.Column<string>(type: "text", nullable: true),
                    ConsentExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Counterparties",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Inn = table.Column<string>(type: "text", nullable: true),
                    Kpp = table.Column<string>(type: "text", nullable: true),
                    DefaultCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Accounts = table.Column<string>(type: "text", nullable: false),
                    Aliases = table.Column<string>(type: "text", nullable: false),
                    Phones = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counterparties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditCards",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditLimitAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    CreditLimitCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    StatementDay = table.Column<int>(type: "integer", nullable: false),
                    MinPaymentAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    MinPaymentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    DebtAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    DebtCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deposits",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    OpenedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    MaturityAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Capitalization = table.Column<int>(type: "integer", nullable: false),
                    Replenishable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deposits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Institutions",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Bic = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "text", nullable: false),
                    Isin = table.Column<string>(type: "text", nullable: true),
                    Figi = table.Column<string>(type: "text", nullable: true),
                    Uid = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LotSize = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentOperations",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AmountAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    AmountCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    PriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ExternalConnector = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LedgerTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    PrincipalCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    IssuedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    MaturityAt = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentDay = table.Column<int>(type: "integer", nullable: false),
                    MonthlyPaymentAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    MonthlyPaymentCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    OutstandingDebtAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    OutstandingDebtCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    AveragePriceAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    AveragePriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CurrentPriceAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    CurrentPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MarketValueAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    MarketValueCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    UnrealizedPnlAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    UnrealizedPnlCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Inn = table.Column<string>(type: "text", nullable: true),
                    Ogrn = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawRecords",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Field = table.Column<int>(type: "integer", nullable: false),
                    Match = table.Column<int>(type: "integer", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HitCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Secrets",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Secrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: true),
                    ImportedTransactions = table.Column<int>(type: "integer", nullable: false),
                    SkippedDuplicates = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BookedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AmountAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    AmountCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountInBaseAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    AmountInBaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    Mcc = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExternalConnector = table.Column<int>(type: "integer", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CounterpartyRaw = table.Column<string>(type: "text", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategorySource = table.Column<int>(type: "integer", nullable: false),
                    CategoryConfidence = table.Column<decimal>(type: "numeric", nullable: true),
                    ProposedCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUser = table.Column<bool>(type: "boolean", nullable: false),
                    TransferLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferLinks",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutgoingTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncomingTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BaseCurrency = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "cashflow",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "cashflow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cashflow",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "cashflow",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cashflow",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "cashflow",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "cashflow",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cashflow",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "cashflow",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cashflow",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId_ProfileId",
                schema: "cashflow",
                table: "Accounts",
                columns: new[] { "UserId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "cashflow",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "cashflow",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "cashflow",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "cashflow",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "cashflow",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSnapshots_AccountId_At",
                schema: "cashflow",
                table: "BalanceSnapshots",
                columns: new[] { "AccountId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_Code",
                schema: "cashflow",
                table: "Categories",
                columns: new[] { "UserId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_UserId_ProfileId",
                schema: "cashflow",
                table: "Connections",
                columns: new[] { "UserId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_UserId",
                schema: "cashflow",
                table: "Counterparties",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditCards_AccountId",
                schema: "cashflow",
                table: "CreditCards",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_AccountId",
                schema: "cashflow",
                table: "Deposits",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_Code",
                schema: "cashflow",
                table: "Institutions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Figi",
                schema: "cashflow",
                table: "Instruments",
                column: "Figi");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Ticker",
                schema: "cashflow",
                table: "Instruments",
                column: "Ticker");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentOperations_AccountId_At",
                schema: "cashflow",
                table: "InvestmentOperations",
                columns: new[] { "AccountId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_AccountId",
                schema: "cashflow",
                table: "Loans",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_AccountId_InstrumentId",
                schema: "cashflow",
                table: "Positions",
                columns: new[] { "AccountId", "InstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                schema: "cashflow",
                table: "Profiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawRecords_ConnectionId",
                schema: "cashflow",
                table: "RawRecords",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_UserId",
                schema: "cashflow",
                table: "Rules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Secrets_UserId",
                schema: "cashflow",
                table: "Secrets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_ConnectionId",
                schema: "cashflow",
                table: "SyncRuns",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_PostedAt",
                schema: "cashflow",
                table: "Transactions",
                columns: new[] { "AccountId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                schema: "cashflow",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CounterpartyId",
                schema: "cashflow",
                table: "Transactions",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DedupeKey",
                schema: "cashflow",
                table: "Transactions",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferLinks_IncomingTransactionId",
                schema: "cashflow",
                table: "TransferLinks",
                column: "IncomingTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferLinks_OutgoingTransactionId",
                schema: "cashflow",
                table: "TransferLinks",
                column: "OutgoingTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "cashflow",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "cashflow",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "BalanceSnapshots",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Categories",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Connections",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Counterparties",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "CreditCards",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Deposits",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Institutions",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Instruments",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "InvestmentOperations",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Loans",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Positions",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Profiles",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "RawRecords",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Rules",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Secrets",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "SyncRuns",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Transactions",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "TransferLinks",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "cashflow");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "cashflow");
        }
    }
}
