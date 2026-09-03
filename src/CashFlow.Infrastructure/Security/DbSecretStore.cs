using System.Text.Json;
using CashFlow.Application;
using CashFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Infrastructure.Security;

/// <summary>Секрет в БД. Payload — зашифрованный JSON словаря.</summary>
public sealed class SecretEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string UserId { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DbSecretStore : ISecretStore
{
    private readonly CashFlowDbContext _db;
    private readonly IFieldEncryptor _enc;

    public DbSecretStore(CashFlowDbContext db, IFieldEncryptor enc)
    {
        _db = db;
        _enc = enc;
    }

    public async Task<string> PutAsync(string userId, IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default)
    {
        var e = new SecretEntry { UserId = userId, Payload = _enc.Encrypt(JsonSerializer.Serialize(secrets)) };
        _db.Secrets.Add(e);
        await _db.SaveChangesAsync(ct);
        return e.Id.ToString("N");
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string userId, string credentialRef, CancellationToken ct = default)
    {
        var id = Guid.ParseExact(credentialRef, "N");
        var e = await _db.Secrets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct)
            ?? throw new UnauthorizedAccessException("Secret not found");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(_enc.Decrypt(e.Payload)) ?? new();
    }

    public async Task DeleteAsync(string userId, string credentialRef, CancellationToken ct = default)
    {
        var id = Guid.ParseExact(credentialRef, "N");
        await _db.Secrets.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
