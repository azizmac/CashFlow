// Сквозная проверка механики импорта на реальной PostgreSQL: подключения по форматам, объединение счетов, дедуп при повторной загрузке.
// Использование: dotnet run --project tools/StatementProbe -- --db "Host=localhost;Port=5439;Database=probe;Username=probe;Password=probe" <файлы...>
using CashFlow.Application;
using CashFlow.Application.Import;
using CashFlow.Application.Seed;
using CashFlow.Connectors.Statements;
using CashFlow.Domain.Identity;
using CashFlow.Domain.Ledger;
using CashFlow.Infrastructure;
using CashFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class ImportProbe
{
    public static async Task RunAsync(string connectionString, List<string> files)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Encryption:MasterKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
        services.AddCashFlowInfrastructure(config);
        services.AddCashFlowApplication();
        services.AddStatementParsers();
        await using var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CashFlowDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
        }

        const string userId = "probe-user";
        Guid profileId;
        using (var scope = sp.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var profile = uow.Profiles.Query().FirstOrDefault(p => p.UserId == userId);
            if (profile is null)
            {
                profile = new FinancialProfile(userId, ProfileKind.SoleProprietor, "ИП (проба)");
                await uow.Profiles.AddAsync(profile);
                await uow.SaveChangesAsync();
            }
            profileId = profile.Id;
        }

        for (var pass = 1; pass <= 2; pass++)
        {
            Console.WriteLine($"\n=========== проход {pass} {(pass == 2 ? "(повторная загрузка — ожидаются только дубликаты)" : "")}");
            foreach (var f in files)
            {
                using var scope = sp.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<StatementImportService>();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await using var fs = File.OpenRead(f);
                    var r = await importer.ImportAsync(userId, profileId, fs, Path.GetFileName(f), null, default);
                    Console.WriteLine($"  {Path.GetFileName(f)}\n    парсер: {r.ParserName}\n    подключение: «{r.Connection.Name}» [{r.Connection.SourceCode}]\n    счёт: «{r.Account.Name}» …{r.Account.AccountNumber?[^4..]} ({r.Account.Type})\n    новых {r.Summary.Imported}, обновлено {r.Summary.Updated}, дубликатов {r.Summary.SkippedDuplicates}, контрагентов +{r.Summary.CounterpartiesCreated}, переводов {r.Summary.TransfersLinked}, категоризировано {r.Summary.Categorized}; {sw.ElapsedMilliseconds} ms");
                    foreach (var w in r.Warnings.Take(2)) Console.WriteLine($"    ! {w}");
                }
                catch (Exception ex) { Console.WriteLine($"  {Path.GetFileName(f)}: ОШИБКА {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        using (var scope = sp.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Console.WriteLine("\n=========== итог в базе");
            var inst = uow.Institutions.Query().ToDictionary(i => i.Id, i => i.Code);
            foreach (var c in uow.Connections.Query().Where(c => c.UserId == userId).ToList())
            {
                var runs = uow.SyncRuns.Query().Count(s => s.ConnectionId == c.Id);
                Console.WriteLine($"  подключение «{c.Name}» банк={inst[c.InstitutionId]} формат={c.SourceCode} статус={c.Status} курсор={c.SyncCursor} импортов={runs}");
            }
            foreach (var a in uow.Accounts.Query().Where(a => a.UserId == userId).ToList())
            {
                var n = uow.Transactions.Query().Count(t => t.AccountId == a.Id);
                var transfers = uow.Transactions.Query().Count(t => t.AccountId == a.Id && t.Kind == TransactionKind.Transfer);
                var conn = uow.Connections.Query().First(c => c.Id == a.ConnectionId);
                Console.WriteLine($"  счёт «{a.Name}» …{a.AccountNumber?[^4..]} {a.Type} {a.Currency} баланс={a.LastBalance?.Amount:N2} операций={n} переводов={transfers} привязан к «{conn.Name}»");
            }
            var cps = uow.Counterparties.Query().Where(c => c.UserId == userId).ToList();
            Console.WriteLine($"  контрагентов: {cps.Count}; «Я (свои счета)»: {(cps.FirstOrDefault(c => c.Kind == CounterpartyKind.Self) is { } self ? $"счетов {self.Accounts.Count}" : "нет")}");
            Console.WriteLine($"  связей переводов: {uow.TransferLinks.Query().Count()}");
            var byKind = cps.GroupBy(c => c.Kind).Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"  контрагенты по типам: {string.Join(", ", byKind)}");
            var topCp = uow.Transactions.Query().Where(t => t.CounterpartyId != null).GroupBy(t => t.CounterpartyId).Select(g => new { Id = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(8).ToList();
            foreach (var t in topCp) Console.WriteLine($"    {cps.First(c => c.Id == t.Id).DisplayName}: {t.N}");
            var uncategorized = uow.Transactions.Query().Count(t => t.CategoryId == null && t.Kind != TransactionKind.Transfer);
            var total = uow.Transactions.Query().Count();
            Console.WriteLine($"  операций всего: {total}, без категории: {uncategorized}");
        }
    }
}
