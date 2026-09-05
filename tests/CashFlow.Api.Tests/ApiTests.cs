using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CashFlow.Application.Contracts;

namespace CashFlow.Api.Tests;

/// <summary>
/// Граница DTO и изоляция пользователей через настоящий REST: регистрация, импорт вымышленной 1С-выписки,
/// маскирование, чужие данные → 404/403, русские ошибки. Все реквизиты в примере вымышленные.
/// </summary>
public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ApiTests(ApiFactory f) => _f = f;

    private const string OwnAccount = "40802810000000000001";

    /// <summary>Минимальная выгрузка 1CClientBankExchange: поступление от ООО, перевод себе на карту, покупка по бизнес-карте.</summary>
    private static readonly string Sample1C = """
        1CClientBankExchange
        ВерсияФормата=1.03
        Кодировка=Windows
        Отправитель=Сбербанк Бизнес Онлайн
        Получатель=
        ДатаСоздания=03.09.2026
        ДатаНачала=01.08.2026
        ДатаКонца=28.08.2026
        РасчСчет=40802810000000000001
        СекцияРасчСчет
        ДатаНачала=28.08.2026
        ДатаКонца=28.08.2026
        НачальныйОстаток=51000.00
        РасчСчет=40802810000000000001
        ВсегоСписано=250.00
        ВсегоПоступило=0
        КонечныйОстаток=50750.00
        КонецРасчСчет
        СекцияДокумент=Платежное поручение
        Номер=101
        Дата=13.08.2026
        Сумма=250000.00
        ПлательщикСчет=40702810000000000002
        ДатаСписано=
        Плательщик=ИНН 7700000005 ООО "РОМАШКА"
        ПлательщикИНН=7700000005
        ПлательщикКПП=770401001
        ПлательщикБИК=044525225
        ПлательщикБанк1=ПАО СБЕРБАНК
        ПолучательСчет=40802810000000000001
        ДатаПоступило=13.08.2026
        Получатель=ИП ИВАНОВ И.И.
        ПолучательКПП=0
        ПолучательБанк1=ОТДЕЛЕНИЕ N0001 ПАО СБЕРБАНК
        ПолучательБИК=044525225
        НазначениеПлатежа=Оплата по договору 12 за услуги разработки. НДС не облагается
        КонецДокумента
        СекцияДокумент=Платежное поручение
        Номер=102
        Дата=26.08.2026
        Сумма=5000.00
        ПлательщикСчет=40802810000000000001
        ДатаСписано=26.08.2026
        Плательщик=ИП ИВАНОВ И.И.
        ПлательщикКПП=0
        ПолучательСчет=40817810000000000003
        ДатаПоступило=
        Получатель=ИВАНОВ ИВАН ИВАНОВИЧ
        ПолучательИНН=123456789012
        ПолучательКПП=0
        НазначениеПлатежа=Перевод собственных средств
        КонецДокумента
        СекцияДокумент=Платежное поручение
        Номер=103
        Дата=28.08.2026
        Сумма=250.00
        ПлательщикСчет=40802810000000000001
        ДатаСписано=28.08.2026
        Плательщик=ИП ИВАНОВ И.И.
        ПолучательСчет=30232810900000000000
        Получатель=Отделение №0001 ПАО Сбербанк
        ПолучательИНН=7707083893
        ПолучательБИК=044525225
        ВидОплаты=17
        НазначениеПлатежа=Покупка PURCHASE_CB в ТУ Сбербанка SUPERMARKET 0001 по карте MIR 2202 за 2026-08-28. Держатель ИВАНОВ ИВАН ИВАНОВИЧ.
        КонецДокумента
        КонецФайла
        """;

    static ApiTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static async Task<ImportResultDto> ImportAsync(HttpClient client, Guid profileId)
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.GetEncoding(1251).GetBytes(Sample1C);
        content.Add(new ByteArrayContent(bytes), "files", "kl_to_1c.txt");
        var resp = await client.PostAsync($"/api/import?profileId={profileId}", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var results = await resp.Content.ReadFromJsonAsync<List<ImportResultDto>>(ApiFactory.Json);
        return Assert.Single(results!);
    }

    [Fact]
    public async Task Unauthenticated_requests_get_401()
    {
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/demo")).StatusCode); // демо выключено
    }

    [Fact]
    public async Task OAuth_start_requires_configured_provider_and_callback_rejects_unknown_state()
    {
        var a = await _f.UserClientAsync("oauth@example.com");
        var profile = (await a.GetFromJsonAsync<List<ProfileDto>>("/api/profiles", ApiFactory.Json))![0];

        // Реквизиты приложения банка в тестовом сервере не заданы → понятная ошибка, а не редирект
        var start = await a.PostAsJsonAsync("/api/oauth/sberbusiness/start", new { profileId = profile.Id, name = "Тест" }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
        Assert.Contains("не заданы на сервере", await start.Content.ReadAsStringAsync());

        var unknown = await a.PostAsJsonAsync("/api/oauth/nosuchbank/start", new { profileId = profile.Id }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // Без токена старт недоступен, а callback анонимный: чужой state не проходит
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/oauth/sberbusiness/start", new { profileId = profile.Id }, ApiFactory.Json)).StatusCode);
        var noRedirect = new HttpClient(_f.Server.CreateHandler()) { BaseAddress = anon.BaseAddress };
        var cb = await noRedirect.GetAsync("/oauth/sberbusiness/callback?state=fake&code=fake");
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Contains("oauth=error", cb.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Register_validation_errors_are_in_russian()
    {
        var anon = _f.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/auth/register", new { email = "weak@example.com", password = "short" }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Пароль должен быть не короче", body);
    }

    [Fact]
    public async Task Import_then_read_only_dtos_and_masked_numbers()
    {
        var a = await _f.UserClientAsync("alice@example.com");
        var profiles = await a.GetFromJsonAsync<List<ProfileDto>>("/api/profiles", ApiFactory.Json);
        var profile = Assert.Single(profiles!);
        Assert.Equal("Личное", profile.Name);

        var import = await ImportAsync(a, profile.Id);
        Assert.Equal(3, import.Imported);
        Assert.Equal(0, import.SkippedDuplicates);

        // Повторная загрузка того же файла — только дубликаты
        var again = await ImportAsync(a, profile.Id);
        Assert.Equal(0, again.Imported);
        Assert.Equal(3, again.SkippedDuplicates);

        // Счёт наружу уходит маскированным: полного номера в JSON нет
        var accountsJson = await a.GetStringAsync("/api/accounts");
        Assert.DoesNotContain(OwnAccount, accountsJson);
        var accounts = JsonSerializer.Deserialize<List<AccountDto>>(accountsJson, ApiFactory.Json)!;
        var acc = Assert.Single(accounts);
        Assert.EndsWith("0001", acc.NumberMasked);
        Assert.Equal(50750m, acc.Balance);

        var txJson = await a.GetStringAsync("/api/transactions?from=2026-08-01&to=2026-08-31&includeTransfers=true");
        Assert.DoesNotContain("40817810000000000003", txJson); // счёт контрагента тоже не отдаём полностью
        var tx = JsonSerializer.Deserialize<List<TransactionDto>>(txJson, ApiFactory.Json)!;
        Assert.Equal(3, tx.Count);
        var income = tx.Single(t => t.Amount == 250000m);
        Assert.Equal("ООО \"РОМАШКА\"", income.CounterpartyName);

        var detail = await a.GetFromJsonAsync<TransactionDetailDto>($"/api/transactions/{income.Id}", ApiFactory.Json);
        Assert.NotNull(detail!.Counterparty);
        Assert.Equal("7700000005", detail.Counterparty!.Inn);
        Assert.Equal("kl_to_1c.txt", detail.Source.FileName);

        var summary = await a.GetFromJsonAsync<SummaryDto>("/api/summary?from=2026-08-01&to=2026-08-31", ApiFactory.Json);
        Assert.Equal(250000m, summary!.Income);
        Assert.Contains(summary.ByCounterparty, c => c.Name.Contains("РОМАШКА") || c.Name.Contains("SUPERMARKET"));

        var coverage = await a.GetFromJsonAsync<List<AccountCoverageDto>>("/api/coverage", ApiFactory.Json);
        Assert.Equal(3, Assert.Single(coverage!).Count);
    }

    [Fact]
    public async Task Other_user_cannot_see_or_change_data()
    {
        var a = await _f.UserClientAsync("owner@example.com");
        var b = await _f.UserClientAsync("intruder@example.com");
        var profile = (await a.GetFromJsonAsync<List<ProfileDto>>("/api/profiles", ApiFactory.Json))![0];
        await ImportAsync(a, profile.Id);
        var tx = (await a.GetFromJsonAsync<List<TransactionDto>>("/api/transactions?from=2026-08-01&to=2026-08-31&includeTransfers=true", ApiFactory.Json))![0];
        var acc = (await a.GetFromJsonAsync<List<AccountDto>>("/api/accounts", ApiFactory.Json))![0];

        Assert.Empty((await b.GetFromJsonAsync<List<AccountDto>>("/api/accounts", ApiFactory.Json))!);
        Assert.Empty((await b.GetFromJsonAsync<List<TransactionDto>>("/api/transactions?from=2026-08-01&to=2026-08-31", ApiFactory.Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/transactions/{tx.Id}")).StatusCode);

        var setCat = await b.PutAsJsonAsync($"/api/transactions/{tx.Id}/category", new { categoryId = (Guid?)null, applyToCounterparty = false }, ApiFactory.Json);
        Assert.Contains(setCat.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });

        var flags = await b.PutAsJsonAsync($"/api/accounts/{acc.Id}/flags", new { includeInCashFlow = false, includeInNetWorth = false }, ApiFactory.Json);
        Assert.Contains(flags.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });

        // У владельца всё на месте
        var still = await a.GetFromJsonAsync<List<AccountDto>>("/api/accounts", ApiFactory.Json);
        Assert.True(Assert.Single(still!).IncludeInCashFlow);
    }

    [Fact]
    public async Task Category_command_applies_and_learns_counterparty_default()
    {
        var a = await _f.UserClientAsync("categorizer@example.com");
        var profile = (await a.GetFromJsonAsync<List<ProfileDto>>("/api/profiles", ApiFactory.Json))![0];
        await ImportAsync(a, profile.Id);
        var categories = await a.GetFromJsonAsync<List<CategoryDto>>("/api/categories", ApiFactory.Json);
        var income = (await a.GetFromJsonAsync<List<TransactionDto>>("/api/transactions?from=2026-08-01&to=2026-08-31", ApiFactory.Json))!.Single(t => t.Amount == 250000m);
        var incomeCat = categories!.First(c => c.Kind == CashFlow.Domain.Ledger.CategoryKind.Income);

        var resp = await a.PutAsJsonAsync($"/api/transactions/{income.Id}/category", new { categoryId = incomeCat.Id, applyToCounterparty = true }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var detail = await a.GetFromJsonAsync<TransactionDetailDto>($"/api/transactions/{income.Id}", ApiFactory.Json);
        Assert.Equal(incomeCat.Id, detail!.Transaction.CategoryId);
        Assert.Equal(incomeCat.Id, detail.Counterparty!.DefaultCategoryId);

        var stats = await a.GetFromJsonAsync<List<CounterpartyStatsDto>>("/api/counterparties?search=РОМАШКА", ApiFactory.Json);
        Assert.Equal(250000m, Assert.Single(stats!).In);
    }
}
