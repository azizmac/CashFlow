using System.Security.Claims;
using CashFlow.Application;
using CashFlow.Application.Contracts;
using CashFlow.Domain.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace CashFlow.Api;

/// <summary>
/// REST API поверх контрактов Application, один эндпоинт на метод интерфейса. Это единственный вход для MAUI-клиента.
/// Аутентификация: cookie (браузер) или bearer-токен Identity (мобильный клиент, /api/auth/login). userId берётся из токена,
/// клиент передать чужой не может. Наружу уходят только DTO из Application.Contracts.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapCashFlowApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{IdentityConstants.BearerScheme}" })
            .AddEndpointFilter<DomainExceptionFilter>();

        // ---------- профили ----------
        api.MapGet("/profiles", (ClaimsPrincipal u, IProfileService s, CancellationToken ct) => s.ListAsync(Uid(u), ct));
        api.MapPost("/profiles", (ClaimsPrincipal u, CreateProfileRequest r, IProfileService s, CancellationToken ct) => s.CreateAsync(Uid(u), r.Kind, r.Name, r.Inn, ct));
        api.MapPut("/profiles/{id:guid}/name", async (ClaimsPrincipal u, Guid id, RenameRequest r, IProfileService s, CancellationToken ct) => { await s.RenameAsync(Uid(u), id, r.Name, ct); return Results.NoContent(); });
        api.MapPut("/profiles/{id:guid}/inn", async (ClaimsPrincipal u, Guid id, SetInnRequest r, IProfileService s, CancellationToken ct) => { await s.SetInnAsync(Uid(u), id, r.Inn, ct); return Results.NoContent(); });

        // ---------- чтение ----------
        api.MapGet("/accounts", (ClaimsPrincipal u, Guid? profileId, ILedgerQueries q, CancellationToken ct) => q.AccountsAsync(Uid(u), profileId, ct));
        api.MapGet("/categories", (ClaimsPrincipal u, ILedgerQueries q, CancellationToken ct) => q.CategoriesAsync(Uid(u), ct));
        api.MapGet("/counterparties", (ClaimsPrincipal u, string? search, CounterpartyKind? kind, ILedgerQueries q, CancellationToken ct) => q.CounterpartiesAsync(Uid(u), search, kind, ct));
        api.MapGet("/counterparties/{id:guid}", async (ClaimsPrincipal u, Guid id, ILedgerQueries q, CancellationToken ct) =>
            await q.CounterpartyAsync(Uid(u), id, ct) is { } c ? Results.Ok(c) : Results.NotFound());
        api.MapGet("/transactions", (ClaimsPrincipal u, [AsParameters] TransactionQuery query, ILedgerQueries q, CancellationToken ct) => q.TransactionsAsync(Uid(u), query.ToFilter(), ct));
        api.MapGet("/transactions/{id:guid}", async (ClaimsPrincipal u, Guid id, ILedgerQueries q, CancellationToken ct) =>
            await q.TransactionAsync(Uid(u), id, ct) is { } t ? Results.Ok(t) : Results.NotFound());
        api.MapGet("/summary", (ClaimsPrincipal u, Guid? profileId, DateOnly from, DateOnly to, ILedgerQueries q, CancellationToken ct) => q.SummaryAsync(Uid(u), profileId, from, to, ct));
        api.MapGet("/coverage", (ClaimsPrincipal u, Guid? profileId, ILedgerQueries q, CancellationToken ct) => q.CoverageAsync(Uid(u), profileId, ct));

        // ---------- команды ----------
        api.MapPut("/transactions/{id:guid}/category", async (ClaimsPrincipal u, Guid id, SetCategoryRequest r, ILedgerCommands c, CancellationToken ct) => { await c.SetCategoryAsync(Uid(u), id, r.CategoryId, r.ApplyToCounterparty, ct); return Results.NoContent(); });
        api.MapPost("/transactions/{id:guid}/accept-proposal", async (ClaimsPrincipal u, Guid id, ILedgerCommands c, CancellationToken ct) => { await c.AcceptProposalAsync(Uid(u), id, ct); return Results.NoContent(); });
        api.MapPut("/transactions/{id:guid}/note", async (ClaimsPrincipal u, Guid id, SetNoteRequest r, ILedgerCommands c, CancellationToken ct) => { await c.SetNoteAsync(Uid(u), id, r.Note, r.Tags, ct); return Results.NoContent(); });
        api.MapPut("/accounts/{id:guid}/flags", async (ClaimsPrincipal u, Guid id, AccountFlagsRequest r, ILedgerCommands c, CancellationToken ct) => { await c.SetAccountFlagsAsync(Uid(u), id, r.IncludeInCashFlow, r.IncludeInNetWorth, ct); return Results.NoContent(); });
        api.MapPost("/accounts/{id:guid}/archive", async (ClaimsPrincipal u, Guid id, ILedgerCommands c, CancellationToken ct) => { await c.ArchiveAccountAsync(Uid(u), id, ct); return Results.NoContent(); });
        api.MapPost("/accounts/manual", (ClaimsPrincipal u, CreateManualAccountRequest r, ILedgerCommands c, CancellationToken ct) => c.CreateManualAccountAsync(Uid(u), r.ProfileId, r.Type, r.Name, r.Currency, r.Balance, ct));
        api.MapPut("/counterparties/{id:guid}/name", async (ClaimsPrincipal u, Guid id, RenameRequest r, ILedgerCommands c, CancellationToken ct) => { await c.RenameCounterpartyAsync(Uid(u), id, r.Name, ct); return Results.NoContent(); });
        api.MapPut("/counterparties/{id:guid}/kind", async (ClaimsPrincipal u, Guid id, SetCounterpartyKindRequest r, ILedgerCommands c, CancellationToken ct) => { await c.SetCounterpartyKindAsync(Uid(u), id, r.Kind, ct); return Results.NoContent(); });
        api.MapPut("/counterparties/{id:guid}/default-category", async (ClaimsPrincipal u, Guid id, SetDefaultCategoryRequest r, ILedgerCommands c, CancellationToken ct) => { await c.SetCounterpartyDefaultCategoryAsync(Uid(u), id, r.CategoryId, ct); return Results.NoContent(); });

        // ---------- категории и правила ----------
        api.MapPost("/categories", (ClaimsPrincipal u, CreateCategoryRequest r, ICategoryService s, CancellationToken ct) => s.CreateAsync(Uid(u), r.Name, r.Kind, r.Icon, ct));
        api.MapDelete("/categories/{id:guid}", async (ClaimsPrincipal u, Guid id, ICategoryService s, CancellationToken ct) => { await s.DeleteAsync(Uid(u), id, ct); return Results.NoContent(); });
        api.MapGet("/categories/rules", (ClaimsPrincipal u, ICategoryService s, CancellationToken ct) => s.RulesAsync(Uid(u), ct));
        api.MapPost("/categories/rules", (ClaimsPrincipal u, CreateRuleRequest r, ICategoryService s, CancellationToken ct) => s.AddRuleAsync(Uid(u), r.Field, r.Match, r.Pattern, r.CategoryId, ct));
        api.MapDelete("/categories/rules/{id:guid}", async (ClaimsPrincipal u, Guid id, ICategoryService s, CancellationToken ct) => { await s.DeleteRuleAsync(Uid(u), id, ct); return Results.NoContent(); });

        // ---------- импорт выписок ----------
        api.MapGet("/import/formats", (IImportService s) => s.Formats);
        api.MapPost("/import", async (ClaimsPrincipal u, Guid profileId, string? bank, IFormFileCollection files, IImportService s, CancellationToken ct) =>
        {
            var results = new List<ImportResultDto>();
            foreach (var f in files)
            {
                await using var stream = f.OpenReadStream();
                results.Add(await s.ImportAsync(Uid(u), profileId, stream, f.FileName, string.IsNullOrWhiteSpace(bank) ? null : bank, ct));
            }
            return Results.Ok(results);
        }).DisableAntiforgery();

        // ---------- подключения ----------
        api.MapGet("/connections", (ClaimsPrincipal u, IConnectionsService s, CancellationToken ct) => s.ListAsync(Uid(u), ct));
        api.MapGet("/connections/connectors", (IConnectionsService s) => s.Connectors());
        api.MapGet("/connections/{id:guid}/runs", (ClaimsPrincipal u, Guid id, int? take, IConnectionsService s, CancellationToken ct) => s.RunsAsync(Uid(u), id, take ?? 10, ct));
        api.MapPost("/connections", (ClaimsPrincipal u, CreateConnectionRequest r, IConnectionsService s, CancellationToken ct) => s.CreateAsync(Uid(u), r.ProfileId, r.Type, r.Name, r.Secrets, ct));
        api.MapPost("/connections/{id:guid}/sync", (ClaimsPrincipal u, Guid id, int? initialDays, IConnectionsService s, CancellationToken ct) => s.SyncAsync(Uid(u), id, initialDays ?? 365, ct));
        api.MapDelete("/connections/{id:guid}", async (ClaimsPrincipal u, Guid id, IConnectionsService s, CancellationToken ct) => { await s.DeleteAsync(Uid(u), id, ct); return Results.NoContent(); });

        return app;
    }

    private static string Uid(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}

/// <summary>Параметры списка операций из query string.</summary>
public sealed class TransactionQuery
{
    public Guid? ProfileId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CounterpartyId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    // Nullable: при [AsParameters] ненулевые свойства становятся обязательными параметрами query string
    public bool? OnlyUncategorized { get; set; }
    public bool? IncludeTransfers { get; set; }
    public int? Take { get; set; }

    public TransactionFilter ToFilter()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new TransactionFilter(ProfileId, AccountId, CategoryId, CounterpartyId, From ?? today.AddMonths(-3), To ?? today, Search,
            OnlyUncategorized ?? false, IncludeTransfers ?? false, Math.Clamp(Take ?? 500, 1, 5000));
    }
}

/// <summary>Исключения прикладного слоя → коды ответа. Тексты ошибок бизнес-логики отдаём как есть, остальные не раскрываем.</summary>
public sealed class DomainExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (UnauthorizedAccessException) { return Results.Json(new ApiError("Нет доступа к объекту"), statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException ex) { return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status404NotFound); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        { return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status400BadRequest); }
    }
}
