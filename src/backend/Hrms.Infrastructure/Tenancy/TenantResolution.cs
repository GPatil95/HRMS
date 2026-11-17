using System.Collections.Concurrent;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hrms.Infrastructure.Tenancy;

/// <summary>Host → tenant lookup from the Platform DB (short-lived local cache, invalidated on tenant/entitlement change).</summary>
public sealed class TenantRegistry(IServiceScopeFactory scopes, IMemoryCache cache, ICacheInvalidationBus bus) : ITenantRegistry
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public async Task<TenantContext?> FindByHostAsync(string host, CancellationToken ct)
    {
        var h = host.Trim().ToLowerInvariant();
        if (cache.TryGetValue($"tenant:host:{h}", out Guid id))
        {
            return await FindByIdAsync(id, ct);
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var tenantId = await db.TenantDomains.AsNoTracking().Where(d => d.HostName == h).Select(d => (Guid?)d.TenantId).FirstOrDefaultAsync(ct);
        if (tenantId is null)
        {
            return null;
        }

        cache.Set($"tenant:host:{h}", tenantId.Value, Ttl);
        return await FindByIdAsync(tenantId.Value, ct);
    }

    public async Task<TenantContext?> FindByIdAsync(Guid tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue($"tenant:id:{tenantId}", out TenantContext? ctx))
        {
            return ctx;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        ctx = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new TenantContext(t.Id, t.TenantNumber, t.Code, t.Name, t.Status, t.EntitlementVersion)).FirstOrDefaultAsync(ct);
        if (ctx is not null)
        {
            cache.Set($"tenant:id:{tenantId}", ctx, Ttl);
        }

        return ctx;
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct)
    {
        Evict(cache, tenantId);
        await bus.PublishAsync(CacheChannels.Tenant, tenantId.ToString(), ct);
    }

    internal static void Evict(IMemoryCache cache, Guid tenantId) => cache.Remove($"tenant:id:{tenantId}");
}

public static class CacheChannels
{
    public const string Tenant = "hrms:invalidate:tenant";
}

/// <summary>Scoped holder for the resolved tenant connection string (never exposed outside infrastructure).</summary>
public sealed class TenantConnectionHolder
{
    public string? ConnectionString { get; private set; }
    public Guid? TenantId { get; private set; }

    internal void Set(Guid tenantId, string cs)
    {
        if (TenantId is { } existing && existing != tenantId)
        {
            throw new InvalidOperationException("Tenant connection is immutable within a scope.");
        }

        TenantId = tenantId;
        ConnectionString = cs;
    }
}

/// <summary>
/// Resolves the tenant database from the Platform registry using the trusted TenantContext only, loads credentials from the
/// secret store, and verifies the database's ops.tenant_identity row matches before first use (defence against mis-wiring).
/// </summary>
public sealed class TenantDatabaseResolver(
    IServiceScopeFactory scopes,
    TenantConnectionHolder holder,
    ITenantDatabaseProvisioner provisioner) : ITenantDatabaseResolver
{
    private static readonly ConcurrentDictionary<Guid, string> Verified = new();

    public async Task EnsureResolvedAsync(TenantContext tenant, CancellationToken ct)
    {
        if (holder.TenantId == tenant.TenantId)
        {
            return;
        }

        if (!Verified.TryGetValue(tenant.TenantId, out var cs))
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            var row = await db.TenantDatabases.AsNoTracking().Include(d => d.Cluster).FirstOrDefaultAsync(d => d.TenantId == tenant.TenantId, ct)
                      ?? throw new TenantUnavailableException("Tenant database is not registered.");
            if (row.Status != TenantDatabaseStatus.Online)
            {
                throw new TenantUnavailableException("Tenant database is not online.");
            }

            var password = await secrets.GetAsync(row.AppSecretRef, ct) ?? throw new TenantUnavailableException("Tenant credentials unavailable.");
            cs = provisioner.BuildConnectionString(row.Cluster!, row.DatabaseName, row.AppRoleName, password);
            if (!await provisioner.VerifyAsync(cs, tenant.TenantId, ct))
            {
                throw new TenantUnavailableException("Tenant database identity mismatch.");
            }

            Verified[tenant.TenantId] = cs;
        }

        holder.Set(tenant.TenantId, cs);
    }

    public void Invalidate(Guid tenantId) => Verified.TryRemove(tenantId, out _);

    internal static void ClearAll()
    {
        Verified.Clear();
        NpgsqlConnection.ClearAllPools();
    }
}

public sealed class TenantUnavailableException(string message) : Exception(message);

public sealed class PlatformSequences(PlatformDbContext db) : IPlatformSequences
{
    public async Task<int> NextTenantNumberAsync(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<int>("SELECT nextval('platform.tenant_number_seq')::int AS \"Value\"").SingleAsync(ct);
}
