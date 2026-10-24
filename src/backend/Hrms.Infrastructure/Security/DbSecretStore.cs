using System.Security.Cryptography;
using System.Text;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hrms.Infrastructure.Security;

public sealed class SecretStoreOptions
{
    public const string Section = "Secrets";
    /// <summary>"Database" (AES-256-GCM envelope in the Platform DB; dev/test) or "AwsSecretsManager" (production).</summary>
    public string Provider { get; set; } = "Database";
    /// <summary>Base64 256-bit master key. Supplied via environment/secret manager only — never committed.</summary>
    public string? MasterKey { get; set; }
}

/// <summary>Development/test secret store: AES-256-GCM ciphertext in ops.secrets, keyed by an externally supplied master key.</summary>
public sealed class DbSecretStore(PlatformDbContext db, IOptions<SecretStoreOptions> options) : ISecretStore
{
    public string Provider => "db";

    public string BuildRef(string name) => $"db:{name}";

    public async Task<string?> GetAsync(string secretRef, CancellationToken ct)
    {
        var row = await db.Secrets.AsNoTracking().FirstOrDefaultAsync(s => s.Ref == secretRef, ct);
        if (row is null)
        {
            return null;
        }

        using var aes = new AesGcm(Key(), 16);
        var plain = new byte[row.Ciphertext.Length];
        aes.Decrypt(row.Nonce, row.Ciphertext, row.Tag, plain, Encoding.UTF8.GetBytes(secretRef));
        return Encoding.UTF8.GetString(plain);
    }

    public async Task SetAsync(string secretRef, string value, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key(), 16))
        {
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(secretRef));
        }

        var row = await db.Secrets.FirstOrDefaultAsync(s => s.Ref == secretRef, ct);
        if (row is null)
        {
            db.Secrets.Add(new SecretEntry { Ref = secretRef, Ciphertext = cipher, Nonce = nonce, Tag = tag, CreatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Ciphertext = cipher;
            row.Nonce = nonce;
            row.Tag = tag;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private byte[] Key()
    {
        var key = options.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Secrets:MasterKey is not configured.");
        }

        var bytes = Convert.FromBase64String(key);
        return bytes.Length == 32 ? bytes : throw new InvalidOperationException("Secrets:MasterKey must be 32 bytes (base64).");
    }
}
