using CashFlow.Application.Contracts;
using CashFlow.Application.Ledger;
using CashFlow.Domain.Identity;

namespace CashFlow.Application.Identity;

/// <summary>Финансовые профили пользователя. У пользователя всегда есть хотя бы один — «Личное».</summary>
public sealed class ProfileService : IProfileService
{
    private readonly IUnitOfWork _uow;
    public ProfileService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<ProfileDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var list = _uow.Profiles.Query().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt).ToList();
        if (list.Count == 0)
        {
            var p = new FinancialProfile(userId, ProfileKind.Individual, "Личное");
            await _uow.Profiles.AddAsync(p, ct);
            await _uow.SaveChangesAsync(ct);
            list.Add(p);
        }
        return list.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProfileDto> CreateAsync(string userId, ProfileKind kind, string name, string? inn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Название обязательно", nameof(name));
        var p = new FinancialProfile(userId, kind, name.Trim(), CleanInn(inn));
        await _uow.Profiles.AddAsync(p, ct);
        await _uow.SaveChangesAsync(ct);
        return p.ToDto();
    }

    public async Task RenameAsync(string userId, Guid profileId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var p = await OwnAsync(userId, profileId, ct);
        p.Rename(name.Trim());
        await _uow.SaveChangesAsync(ct);
    }

    public async Task SetInnAsync(string userId, Guid profileId, string? inn, CancellationToken ct = default)
    {
        var p = await OwnAsync(userId, profileId, ct);
        p.SetIdentifiers(CleanInn(inn), p.Ogrn);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<FinancialProfile> OwnAsync(string userId, Guid id, CancellationToken ct)
    {
        var p = await _uow.Profiles.FindAsync(id, ct);
        if (p is null || p.UserId != userId) throw new UnauthorizedAccessException();
        return p;
    }

    /// <summary>ИНН — 10 цифр у организаций, 12 у физлиц и ИП; всё остальное считаем пустым.</summary>
    public static string? CleanInn(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        return d.Length is 10 or 12 ? d : null;
    }
}
