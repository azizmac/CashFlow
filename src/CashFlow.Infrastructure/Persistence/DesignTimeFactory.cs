using CashFlow.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CashFlow.Infrastructure.Persistence;

/// <summary>Только для `dotnet ef migrations add`. К БД не подключается.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CashFlowDbContext>
{
    public CashFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CashFlowDbContext>()
            .UseNpgsql("Host=localhost;Database=cashflow;Username=cashflow;Password=design", n => n.MigrationsHistoryTable("__EFMigrationsHistory", "cashflow"))
            .Options;
        return new CashFlowDbContext(options, new AesGcmFieldEncryptor(new byte[32]));
    }
}
