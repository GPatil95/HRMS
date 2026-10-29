using Hrms.Domain.Platform;

namespace Hrms.Application.Abstractions;

public interface ISecretStore
{
    string Provider { get; }
    Task<string?> GetAsync(string secretRef, CancellationToken ct);
    Task SetAsync(string secretRef, string value, CancellationToken ct);
    string BuildRef(string name);
}

/// <summary>The resolved platform master key plus safe diagnostics. Key material never leaves the process.</summary>
public sealed record MasterKeyInfo(byte[] Key, string Source, string Fingerprint);

/// <summary>Master-key configuration is missing, conflicting or invalid — a hard failure, never silently worked around.</summary>
public sealed class MasterKeyException(string message) : InvalidOperationException(message);

/// <summary>
/// Single source of truth for the platform master key. Implementations resolve it from the configured
/// source (env var, key file, or — in production — an external secret provider); callers never re-implement
/// key loading. Key access fails fast on any misconfiguration.
/// </summary>
public interface IPlatformMasterKeyProvider
{
    /// <summary>Resolved key + source description + short SHA-256 fingerprint (safe to log/display).</summary>
    MasterKeyInfo Key { get; }
}

public interface ITenantRegistry
{
    Task<TenantContext?> FindByHostAsync(string host, CancellationToken ct);
    Task<TenantContext?> FindByIdAsync(Guid tenantId, CancellationToken ct);
    Task InvalidateAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Resolves (and verifies) the tenant database for a trusted tenant context. Credentials never leave process memory.</summary>
public interface ITenantDatabaseResolver
{
    Task EnsureResolvedAsync(TenantContext tenant, CancellationToken ct);
    void Invalidate(Guid tenantId);
}

public sealed record ClusterConnection(string Host, int Port, string Username, string Password);

public interface ITenantDatabaseProvisioner
{
    Task EnsureRoleAsync(ClusterConnection admin, string roleName, string password, CancellationToken ct);
    Task EnsureDatabaseAsync(ClusterConnection admin, string databaseName, string ownerRole, CancellationToken ct);
    Task ConfigureDatabaseAsync(ClusterConnection admin, string databaseName, string migratorRole, string appRole, CancellationToken ct);
    Task MigrateAsync(string migratorConnectionString, CancellationToken ct);
    Task ApplyGrantsAsync(string migratorConnectionString, string appRole, CancellationToken ct);
    Task WriteTenantIdentityAsync(string migratorConnectionString, Guid tenantId, int tenantNumber, CancellationToken ct);
    Task<bool> VerifyAsync(string appConnectionString, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(string connectionString, CancellationToken ct);
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(string connectionString, CancellationToken ct);
    string BuildConnectionString(DbCluster cluster, string database, string username, string password);
}

public interface IPlatformSequences
{
    Task<int> NextTenantNumberAsync(CancellationToken ct);
}

public interface IDistributedLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

public interface IRateLimiter
{
    /// <summary>Fixed-window counter. Returns false when the limit is exceeded.</summary>
    Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct);
}

public interface ISessionCache
{
    Task<bool?> IsActiveAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct);
    Task MarkActiveAsync(string realm, Guid? tenantId, Guid sessionId, TimeSpan ttl, CancellationToken ct);
    Task MarkRevokedAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct);
}

/// <summary>Cross-instance cache invalidation (Redis pub/sub).</summary>
public interface ICacheInvalidationBus
{
    Task PublishAsync(string channel, string message, CancellationToken ct);
}

public sealed record StoredFileMetadata(string Key, long SizeBytes, string? ContentType, DateTimeOffset? LastModified);

/// <summary>Low-level object storage abstraction. Business modules use IFileService, never this directly.</summary>
public interface IFileStorageService
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken ct);
}

public sealed record MalwareScanResult(bool IsClean, string? Signature);

public interface IMalwareScanner
{
    Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken ct);
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record OutgoingEmail(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyList<EmailAttachment> Attachments);

public interface IEmailTransport
{
    Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct);
}

public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, object?> model);
}

public interface IPushNotificationSender
{
    Task SendAsync(Guid tenantId, Guid userId, string title, string? body, CancellationToken ct);
}

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(Guid tenantId, Guid userId, string eventName, object payload, CancellationToken ct);
}

public interface ITotpService
{
    string GenerateSecret();
    string BuildProvisioningUri(string issuer, string account, string secret);
    bool Verify(string secret, string code);
}

/// <summary>Data-protection wrapper for secrets at rest (MFA secrets) and short-lived signed payloads.</summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);
    string? Unprotect(string purpose, string protectedValue);
    string ProtectTimed(string purpose, string plaintext, TimeSpan lifetime);
    string? UnprotectTimed(string purpose, string protectedValue);
}

public interface IExcelWriter
{
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows);
}
