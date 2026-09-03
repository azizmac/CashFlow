using CashFlow.Application.Categorization;
using CashFlow.Domain.Connections;
using CashFlow.Domain.Ledger;

namespace CashFlow.Application.Seed;

/// <summary>Сидирует справочники: банки, системные категории, системные правила.</summary>
public sealed class SeedService
{
    private readonly IUnitOfWork _uow;
    public SeedService(IUnitOfWork uow) => _uow = uow;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var institutions = new (string Code, string Name, InstitutionKind Kind, string? Bic)[]
        {
            (Institution.Codes.Sber, "Сбер", InstitutionKind.Bank, "044525225"),
            (Institution.Codes.TBank, "Т-Банк", InstitutionKind.Bank, "044525974"),
            (Institution.Codes.TInvest, "Т-Инвестиции", InstitutionKind.Broker, null),
            (Institution.Codes.Vtb, "ВТБ", InstitutionKind.Bank, "044525187"),
            (Institution.Codes.Alfa, "Альфа-Банк", InstitutionKind.Bank, "044525593"),
            (Institution.Codes.Cash, "Наличные", InstitutionKind.Cash, null),
            (Institution.Codes.Other, "Другой", InstitutionKind.Manual, null),
        };
        var existingInst = _uow.Institutions.Query().Select(i => i.Code).ToHashSet();
        foreach (var i in institutions)
            if (!existingInst.Contains(i.Code)) await _uow.Institutions.AddAsync(new Institution(i.Code, i.Name, i.Kind, i.Bic), ct);

        var existingCats = _uow.Categories.Query().Where(c => c.IsSystem).ToList();
        var byCode = existingCats.Where(c => c.Code != null).ToDictionary(c => c.Code!);
        foreach (var d in SystemCategories.All.Where(d => d.ParentCode is null))
        {
            if (byCode.ContainsKey(d.Code)) continue;
            var c = new Category(null, d.Name, d.Kind, null, d.Icon, d.Color, true, d.Code);
            byCode[d.Code] = c;
            await _uow.Categories.AddAsync(c, ct);
        }
        foreach (var d in SystemCategories.All.Where(d => d.ParentCode is not null))
        {
            if (byCode.ContainsKey(d.Code)) continue;
            var c = new Category(null, d.Name, d.Kind, byCode[d.ParentCode!].Id, d.Icon, d.Color, true, d.Code);
            byCode[d.Code] = c;
            await _uow.Categories.AddAsync(c, ct);
        }

        var existingRules = _uow.Rules.Query().Where(r => r.UserId == null).Select(r => r.Pattern).ToHashSet();
        foreach (var r in SystemRules.All)
        {
            if (existingRules.Contains(r.Pattern) || !byCode.TryGetValue(r.CategoryCode, out var cat)) continue;
            await _uow.Rules.AddAsync(new CategorizationRule(null, r.Field, r.Match, r.Pattern, cat.Id, r.Priority, RuleOrigin.System), ct);
        }

        await _uow.SaveChangesAsync(ct);
    }
}
