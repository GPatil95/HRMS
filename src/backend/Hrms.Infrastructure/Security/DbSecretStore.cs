using System.Security.Cryptography;
using System.Text;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Infrastructure.Security;

public sealed class SecretStoreOptions
{
    public const string Section = "Secrets";
    /// <summary>"Database" (AES-256-GCM envelope in the Platform DB; dev/test) or "AwsSecretsManager" (production).</summary>
    public string Provider { get; set; } = "Database";
    /// <summary>Base64 256-bit master key. Supplied via environment/secret manager only — never committed.</summary>
    public string? MasterKey { get; set; }
    /// <summary>
    /// Path to a file containing the base64 master key — the authoritative source for Docker deployments
    /// (persistent keydata volume at /keys/master.key). The file must exist; it is never auto-generated
    /// (first-time setup is the `initialize-master-key` CLI command). Setting both this and
    /// <see cref="MasterKey"/> is a hard configuration error. In Development, when neither is configured,
    /// a per-user file at ~/.hrms-dev-masterkey is used so `dotnet run`, CLI seeding and the API share one key.
    /// </summary>
    public string? MasterKeyFile { get; set; }
}

/// <summary>Development/test secret store: AES-256-GCM ciphertext in ops.secrets, keyed by the platform master key.</summary>
public sealed class DbSecretStore(PlatformDbContext db, IPlatformMasterKeyProvider masterKey) : ISecretStore
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

    private byte[] Key() => masterKey.Key.Key;
}
