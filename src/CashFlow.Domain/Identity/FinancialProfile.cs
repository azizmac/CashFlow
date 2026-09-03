using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Identity;

public enum ProfileKind { Individual = 0, SoleProprietor = 1, Company = 2 }

/// <summary>Финансовый профиль пользователя: физлицо, ИП или компания. У одного пользователя может быть несколько.</summary>
public sealed class FinancialProfile : Entity
{
    private FinancialProfile() { }

    public FinancialProfile(string userId, ProfileKind kind, string name, string? inn = null)
    {
        UserId = userId;
        Kind = kind;
        Name = name;
        Inn = inn;
    }

    /// <summary>Id пользователя из ASP.NET Identity.</summary>
    public string UserId { get; private set; } = default!;
    public ProfileKind Kind { get; private set; }
    public string Name { get; private set; } = default!;
    /// <summary>ИНН — шифруется в хранилище.</summary>
    public string? Inn { get; private set; }
    public string? Ogrn { get; private set; }

    public void Rename(string name) { Name = name; Touch(); }
    public void SetIdentifiers(string? inn, string? ogrn) { Inn = inn; Ogrn = ogrn; Touch(); }
}
