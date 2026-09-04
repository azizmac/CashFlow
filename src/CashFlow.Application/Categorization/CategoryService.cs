using CashFlow.Application.Contracts;
using CashFlow.Application.Ledger;
using CashFlow.Domain.Ledger;

namespace CashFlow.Application.Categorization;

/// <summary>Свои категории и правила пользователя. Системные категории и правила менять нельзя.</summary>
public sealed class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _uow;
    public CategoryService(IUnitOfWork uow) => _uow = uow;

    public Task<IReadOnlyList<CategoryDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyList<CategoryDto> list = _uow.Categories.Query().Where(c => c.UserId == null || c.UserId == userId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name).ToList().Select(c => c.ToDto()).ToList();
        return Task.FromResult(list);
    }

    public async Task<CategoryDto> CreateAsync(string userId, string name, CategoryKind kind, string? icon, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Название обязательно", nameof(name));
        var c = new Category(userId, name.Trim(), kind, null, string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(), null);
        await _uow.Categories.AddAsync(c, ct);
        await _uow.SaveChangesAsync(ct);
        return c.ToDto();
    }

    public async Task DeleteAsync(string userId, Guid categoryId, CancellationToken ct = default)
    {
        var c = await _uow.Categories.FindAsync(categoryId, ct);
        if (c is null || c.IsSystem || c.UserId != userId) throw new UnauthorizedAccessException();
        _uow.Categories.Remove(c);
        await _uow.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<RuleDto>> RulesAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyList<RuleDto> list = _uow.Rules.Query().Where(r => r.UserId == userId).OrderByDescending(r => r.Priority).ToList().Select(r => r.ToDto()).ToList();
        return Task.FromResult(list);
    }

    public async Task<RuleDto> AddRuleAsync(string userId, RuleField field, RuleMatch match, string pattern, Guid categoryId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Шаблон обязателен", nameof(pattern));
        var cat = await _uow.Categories.FindAsync(categoryId, ct);
        if (cat is null || (cat.UserId is not null && cat.UserId != userId)) throw new UnauthorizedAccessException();
        var r = new CategorizationRule(userId, field, match, pattern.Trim(), categoryId, 100, RuleOrigin.User);
        await _uow.Rules.AddAsync(r, ct);
        await _uow.SaveChangesAsync(ct);
        return r.ToDto();
    }

    public async Task DeleteRuleAsync(string userId, Guid ruleId, CancellationToken ct = default)
    {
        var r = await _uow.Rules.FindAsync(ruleId, ct);
        if (r is null || r.UserId != userId) throw new UnauthorizedAccessException();
        _uow.Rules.Remove(r);
        await _uow.SaveChangesAsync(ct);
    }
}
