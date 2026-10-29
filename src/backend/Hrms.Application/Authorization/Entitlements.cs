using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Hrms.Application.Authorization;

public sealed record TenantEntitlements(
    long Version,
    bool SubscriptionUsable,
    string? SubscriptionStatus,
    IReadOnlyList<string> Modules,
    IReadOnlyDictionary<string, string?> Features)
{
    public bool IsModuleEnabled(string moduleCode) => SubscriptionUsable && Modules.Contains(moduleCode, StringComparer.Ordinal);
    public bool IsFeatureEnabled(string featureCode) => SubscriptionUsable && Features.ContainsKey(featureCode);
}

public interface IEntitlementService
{
    Task<TenantEntitlements> GetAsync(Guid tenantId, CancellationToken ct);
    Task InvalidateAsync(Guid tenantId, CancellationToken ct);
}

public sealed class EntitlementService(IPlatformDbContext db, HybridCache cache, IClock clock) : IEntitlementService
{
    public static string CacheKey(Guid tenantId) => $"plat:ent:{tenantId}";

    public async Task<TenantEntitlements> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromSeconds(30) };
        return await cache.GetOrCreateAsync(CacheKey(tenantId), async c => await LoadAsync(tenantId, c), options, cancellationToken: ct);
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct) => await cache.RemoveAsync(CacheKey(tenantId), ct);

    private async Task<TenantEntitlements> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var version = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.EntitlementVersion).FirstOrDefaultAsync(ct);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(ct);
        var usable = subscriptions.FirstOrDefault(s => s.IsUsable(today));

        var modules = await db.ModuleEntitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsEnabled && e.ValidFrom <= today && (e.ValidTo == null || e.ValidTo >= today))
            .Join(db.Modules, e => e.ModuleId, m => m.Id, (e, m) => m.Code)
            .ToListAsync(ct);

        // Core modules are part of every tenant with a usable subscription.
        var core = await db.Modules.AsNoTracking().Where(m => m.Kind == ModuleKind.Core && m.IsActive).Select(m => m.Code).ToListAsync(ct);

        var features = await db.FeatureEntitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsEnabled && e.ValidFrom <= today && (e.ValidTo == null || e.ValidTo >= today))
            .Join(db.Features, e => e.FeatureId, f => f.Id, (e, f) => new { f.Code, e.Value })
            .ToListAsync(ct);

        return new TenantEntitlements(
            version,
            usable is not null,
            (usable ?? subscriptions.OrderByDescending(s => s.StartsOn).FirstOrDefault())?.Status.ToString(),
            modules.Concat(core).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            features.GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.First().Value));
    }
}
