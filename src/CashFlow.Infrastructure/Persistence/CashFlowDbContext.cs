using System.Text.Json;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Investments;
using CashFlow.Domain.Ledger;
using CashFlow.Domain.Products;
using CashFlow.Domain.Shared;
using CashFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CashFlow.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser
{
    public string BaseCurrency { get; set; } = "RUB";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CashFlowDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IFieldEncryptor _enc;

    public CashFlowDbContext(DbContextOptions<CashFlowDbContext> options, IFieldEncryptor enc) : base(options)
    {
        _enc = enc;
    }

    public DbSet<FinancialProfile> Profiles => Set<FinancialProfile>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<RawRecord> RawRecords => Set<RawRecord>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransferLink> TransferLinks => Set<TransferLink>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategorizationRule> Rules => Set<CategorizationRule>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<InvestmentOperation> InvestmentOperations => Set<InvestmentOperation>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<SecretEntry> Secrets => Set<SecretEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("cashflow");

        var enc = new ValueConverter<string?, string?>(
            v => v == null ? null : _enc.Encrypt(v),
            v => v == null ? null : _enc.Decrypt(v));
        var encList = new ValueConverter<List<string>, string>(
            v => _enc.Encrypt(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null)),
            v => JsonSerializer.Deserialize<List<string>>(_enc.Decrypt(v), (JsonSerializerOptions?)null) ?? new List<string>());
        var listComparer = new ValueComparer<List<string>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())), v => v.ToList());
        var plainList = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        var currency = new ValueConverter<Currency, string>(c => c.Code, s => new Currency(s));
        var dedupe = new ValueConverter<DedupeKey, string>(k => k.Value, s => new DedupeKey(s));

        b.Entity<ApplicationUser>().ToTable("Users");

        b.Entity<FinancialProfile>(e =>
        {
            e.HasIndex(p => p.UserId);
            e.Property(p => p.Inn).HasConversion(enc);
            e.Property(p => p.Ogrn).HasConversion(enc);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Institution>(e =>
        {
            e.HasIndex(i => i.Code).IsUnique();
            e.Property(i => i.Code).HasMaxLength(32);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Connection>(e =>
        {
            e.HasIndex(c => new { c.UserId, c.ProfileId });
            e.Property(c => c.CredentialRef).HasMaxLength(64);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<SyncRun>(e => { e.HasIndex(s => s.ConnectionId); e.Ignore(p => p.DomainEvents); });

        b.Entity<RawRecord>(e =>
        {
            e.HasIndex(r => r.ConnectionId);
            e.Property(r => r.Payload).HasConversion(enc!);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Account>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.ProfileId });
            e.Property(a => a.Currency).HasConversion(currency).HasMaxLength(3);
            e.Property(a => a.AccountNumber).HasConversion(enc);
            e.OwnsOne(a => a.ExternalRef, r =>
            {
                r.Property(x => x.Connector).HasColumnName("ExternalConnector");
                r.Property(x => x.ExternalId).HasColumnName("ExternalId").HasMaxLength(128);
            });
            e.OwnsOne(a => a.LastBalance, m =>
            {
                m.Property(x => x.Amount).HasColumnName("LastBalanceAmount").HasPrecision(20, 4);
                m.Property(x => x.Currency).HasColumnName("LastBalanceCurrency").HasConversion(currency).HasMaxLength(3);
            });
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<BalanceSnapshot>(e =>
        {
            e.HasIndex(s => new { s.AccountId, s.At });
            MapMoney(e.OwnsOne(s => s.Current), "Current", currency);
            MapMoney(e.OwnsOne(s => s.Available), "Available", currency);
            MapMoney(e.OwnsOne(s => s.Blocked), "Blocked", currency);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Transaction>(e =>
        {
            e.HasIndex(t => new { t.AccountId, t.PostedAt });
            e.HasIndex(t => t.DedupeKey).IsUnique();
            e.HasIndex(t => t.CounterpartyId);
            e.HasIndex(t => t.CategoryId);
            e.Property(t => t.DedupeKey).HasConversion(dedupe).HasMaxLength(64);
            e.Property(t => t.Description).HasMaxLength(1000);
            e.Property(t => t.Mcc).HasMaxLength(4);
            MapMoney(e.OwnsOne(t => t.Amount), "Amount", currency);
            MapMoney(e.OwnsOne(t => t.AmountInBase), "AmountInBase", currency);
            e.OwnsOne(t => t.ExternalRef, r =>
            {
                r.Property(x => x.Connector).HasColumnName("ExternalConnector");
                r.Property(x => x.ExternalId).HasColumnName("ExternalId").HasMaxLength(128);
            });
            // Реквизиты контрагента — зашифрованный JSON целиком
            e.Property(t => t.CounterpartyRaw).HasColumnName("CounterpartyRaw").HasConversion(
                new ValueConverter<CounterpartyRaw, string>(
                    v => _enc.Encrypt(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null)),
                    v => JsonSerializer.Deserialize<CounterpartyRaw>(_enc.Decrypt(v), (JsonSerializerOptions?)null) ?? CounterpartyRaw.Empty));
            e.Ignore(t => t.Tags);
            e.Property<List<string>>("_tags").HasColumnName("Tags").HasConversion(plainList, listComparer);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<TransferLink>(e =>
        {
            e.HasIndex(l => l.OutgoingTransactionId).IsUnique();
            e.HasIndex(l => l.IncomingTransactionId).IsUnique();
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Counterparty>(e =>
        {
            e.HasIndex(c => c.UserId);
            e.Property(c => c.DisplayName).HasMaxLength(300);
            e.Property(c => c.Inn).HasConversion(enc);
            e.Property(c => c.Kpp).HasConversion(enc);
            e.Ignore(c => c.Aliases); e.Ignore(c => c.Accounts); e.Ignore(c => c.Phones);
            e.Property<List<string>>("_aliases").HasColumnName("Aliases").HasConversion(plainList, listComparer);
            e.Property<List<string>>("_accounts").HasColumnName("Accounts").HasConversion(encList, listComparer);
            e.Property<List<string>>("_phones").HasColumnName("Phones").HasConversion(encList, listComparer);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(c => new { c.UserId, c.Code });
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Code).HasMaxLength(64);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<CategorizationRule>(e =>
        {
            e.HasIndex(r => r.UserId);
            e.Property(r => r.Pattern).HasMaxLength(500);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Instrument>(e =>
        {
            e.HasIndex(i => i.Ticker);
            e.HasIndex(i => i.Figi);
            e.Property(i => i.Currency).HasConversion(currency).HasMaxLength(3);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Position>(e =>
        {
            e.HasIndex(p => new { p.AccountId, p.InstrumentId }).IsUnique();
            e.Property(p => p.Quantity).HasPrecision(20, 6);
            MapMoney(e.OwnsOne(p => p.AveragePrice), "AveragePrice", currency);
            MapMoney(e.OwnsOne(p => p.CurrentPrice), "CurrentPrice", currency);
            MapMoney(e.OwnsOne(p => p.MarketValue), "MarketValue", currency);
            MapMoney(e.OwnsOne(p => p.UnrealizedPnl), "UnrealizedPnl", currency);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<InvestmentOperation>(e =>
        {
            e.HasIndex(o => new { o.AccountId, o.At });
            e.Property(o => o.Quantity).HasPrecision(20, 6);
            MapMoney(e.OwnsOne(o => o.Amount), "Amount", currency);
            MapMoney(e.OwnsOne(o => o.Price), "Price", currency);
            e.OwnsOne(o => o.ExternalRef, r =>
            {
                r.Property(x => x.Connector).HasColumnName("ExternalConnector");
                r.Property(x => x.ExternalId).HasColumnName("ExternalId").HasMaxLength(128);
            });
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<Deposit>(e => { e.HasIndex(d => d.AccountId).IsUnique(); e.Property(d => d.RatePercent).HasPrecision(8, 4); e.Ignore(p => p.DomainEvents); });
        b.Entity<Loan>(e =>
        {
            e.HasIndex(l => l.AccountId).IsUnique();
            e.Property(l => l.RatePercent).HasPrecision(8, 4);
            MapMoney(e.OwnsOne(l => l.Principal), "Principal", currency);
            MapMoney(e.OwnsOne(l => l.MonthlyPayment), "MonthlyPayment", currency);
            MapMoney(e.OwnsOne(l => l.OutstandingDebt), "OutstandingDebt", currency);
            e.Ignore(p => p.DomainEvents);
        });
        b.Entity<CreditCard>(e =>
        {
            e.HasIndex(c => c.AccountId).IsUnique();
            MapMoney(e.OwnsOne(c => c.CreditLimit), "CreditLimit", currency);
            MapMoney(e.OwnsOne(c => c.MinPayment), "MinPayment", currency);
            MapMoney(e.OwnsOne(c => c.Debt), "Debt", currency);
            e.Ignore(p => p.DomainEvents);
        });

        b.Entity<SecretEntry>(e => { e.ToTable("Secrets"); e.HasIndex(s => s.UserId); });
    }

    private static void MapMoney<T>(OwnedNavigationBuilder<T, Money> m, string prefix, ValueConverter<Currency, string> currency) where T : class
    {
        m.Property(x => x.Amount).HasColumnName(prefix + "Amount").HasPrecision(20, 4);
        m.Property(x => x.Currency).HasColumnName(prefix + "Currency").HasConversion(currency).HasMaxLength(3);
    }
}
