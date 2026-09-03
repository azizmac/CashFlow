using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CashFlow.Infrastructure.Security;

public sealed class EncryptionOptions
{
    public const string Section = "Encryption";

    /// <summary>Base64 32-байтный ключ. Если задан — используется напрямую.</summary>
    public string? MasterKey { get; set; }

    /// <summary>Альтернатива: парольная фраза, из которой ключ выводится Argon2id. Salt обязателен.</summary>
    public string? MasterPassphrase { get; set; }
    public string? Salt { get; set; }
}

/// <summary>
/// Шифрование полей: AES-256-GCM, случайный nonce, формат "enc:v1:{base64(nonce|tag|ciphertext)}".
/// Ключ приходит из окружения (Docker secret / env), в БД не хранится.
/// </summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string stored);
    bool IsEncrypted(string? value);
}

public sealed class AesGcmFieldEncryptor : IFieldEncryptor, IDisposable
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmFieldEncryptor(IOptions<EncryptionOptions> options) : this(ResolveKey(options.Value)) { }

    public AesGcmFieldEncryptor(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes", nameof(key));
        _key = key;
    }

    public static byte[] ResolveKey(EncryptionOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.MasterKey))
        {
            var k = Convert.FromBase64String(o.MasterKey);
            if (k.Length != 32) throw new InvalidOperationException("Encryption:MasterKey must be 32 bytes (base64)");
            return k;
        }
        if (!string.IsNullOrWhiteSpace(o.MasterPassphrase))
        {
            if (string.IsNullOrWhiteSpace(o.Salt) || o.Salt.Length < 16)
                throw new InvalidOperationException("Encryption:Salt (>=16 chars) is required with MasterPassphrase");
            return DeriveKey(o.MasterPassphrase, Encoding.UTF8.GetBytes(o.Salt));
        }
        throw new InvalidOperationException("Encryption is not configured. Set Encryption:MasterKey (base64 32 bytes) or Encryption:MasterPassphrase + Encryption:Salt.");
    }

    public static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            MemorySize = 64 * 1024,
            Iterations = 3,
        };
        return argon.GetBytes(32);
    }

    public static string GenerateKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public bool IsEncrypted(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plaintext)
    {
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, pt, ct, tag);
        var buf = new byte[NonceSize + TagSize + ct.Length];
        Buffer.BlockCopy(nonce, 0, buf, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, buf, NonceSize, TagSize);
        Buffer.BlockCopy(ct, 0, buf, NonceSize + TagSize, ct.Length);
        return Prefix + Convert.ToBase64String(buf);
    }

    public string Decrypt(string stored)
    {
        if (!IsEncrypted(stored)) return stored; // legacy/plaintext tolerance during migration
        var buf = Convert.FromBase64String(stored[Prefix.Length..]);
        var nonce = buf.AsSpan(0, NonceSize);
        var tag = buf.AsSpan(NonceSize, TagSize);
        var ct = buf.AsSpan(NonceSize + TagSize);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
