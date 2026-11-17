using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Hrms.Application.Authorization;

public sealed record ScopeGrant(ScopeLevel Level, ScopeTargetKind? TargetKind, IReadOnlyList<Guid> TargetIds);

public sealed class EffectivePermissionSet
{
    public EffectivePermissionSet(Dictionary<string, List<ScopeGrant>> grants) => Grants = grants;

    public Dictionary<string, List<ScopeGrant>> Grants { get; init; }

    public bool Has(string code) => Grants.ContainsKey(code);

    public IReadOnlyList<ScopeGrant> ScopesFor(string code) =>
        Grants.TryGetValue(code, out var list) ? list : [];

    public IEnumerable<string> Codes => Grants.Keys;
}

public interface IPermissionService
{
    Task<EffectivePermissionSet> GetEffectiveAsync(Guid userId, CancellationToken ct);
    Task<bool> HasAsync(Guid userId, string permissionCode, CancellationToken ct);
    /// <summary>Bump tenant authorization version so cached effective permissions are recomputed everywhere.</summary>
    Task BumpVersionAsync(CancellationToken ct);
}

public sealed class PermissionService(
    ITenantDbContext db,
    ITenantContextAccessor tenant,
    HybridCache cache,
    IClock clock) : IPermissionService
{
    public async Task<EffectivePermissionSet> GetEffectiveAsync(Guid userId, CancellationToken ct)
    {
        var version = await db.AuthorizationState.AsNoTracking().Select(s => s.Version).FirstOrDefaultAsync(ct);
        var key = $"t:{tenant.Required.TenantId}:perm:{userId}:v{version}";
        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(1) };
        var dto = await cache.GetOrCreateAsync(key, async c => await ComputeAsync(userId, c), options, cancellationToken: ct);
        return new EffectivePermissionSet(dto.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
    }

    public async Task<bool> HasAsync(Guid userId, string permissionCode, CancellationToken ct) =>
        (await GetEffectiveAsync(userId, ct)).Has(permissionCode);

    public async Task BumpVersionAsync(CancellationToken ct)
    {
        var state = await db.AuthorizationState.FirstOrDefaultAsync(ct);
        if (state is null)
        {
            db.AuthorizationState.Add(new AuthorizationState { Id = 1, Version = 2 });
        }
        else
        {
            state.Version++;
        }
    }

    internal async Task<Dictionary<string, ScopeGrant[]>> ComputeAsync(Guid userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Status }).FirstOrDefaultAsync(ct);
        var result = new Dictionary<string, List<ScopeGrant>>(StringComparer.Ordinal);
        if (user is null || user.Status != Domain.Common.UserStatus.Active)
        {
            return [];
        }

        var assignments = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today))
            .Select(a => new
            {
                Targets = a.ScopeTargets.Select(t => new { t.Kind, t.TargetId }).ToList(),
                Perms = a.Role!.Permissions.Where(p => p.Permission!.IsActive)
                    .Select(p => new { p.Permission!.Code, p.ScopeLevel }).ToList(),
            })
            .ToListAsync(ct);

        foreach (var a in assignments)
        {
            foreach (var p in a.Perms)
            {
                var kind = TargetKindFor(p.ScopeLevel);
                var ids = kind is null ? [] : a.Targets.Where(t => t.Kind == kind).Select(t => t.TargetId).ToList();
                Add(result, p.Code, new ScopeGrant(p.ScopeLevel, ids.Count > 0 ? kind : null, ids));
            }
        }

        var overrides = await db.PermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == userId && o.ValidFrom <= today && (o.ValidTo == null || o.ValidTo >= today) && o.Permission!.IsActive)
            .Select(o => new { o.Permission!.Code, o.Effect, o.ScopeLevel, o.TargetKind, o.TargetIds })
            .ToListAsync(ct);

        foreach (var o in overrides.Where(o => o.Effect == PermissionEffect.Grant))
        {
            Add(result, o.Code, new ScopeGrant(o.ScopeLevel ?? ScopeLevel.Tenant, o.TargetIds.Length > 0 ? o.TargetKind : null, o.TargetIds));
        }

        // Deny wins: an active deny override removes the permission entirely, whatever roles/grants say.
        foreach (var o in overrides.Where(o => o.Effect == PermissionEffect.Deny))
        {
            result.Remove(o.Code);
        }

        return result.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    public static ScopeTargetKind? TargetKindFor(ScopeLevel level) => level switch
    {
        ScopeLevel.Company => ScopeTargetKind.Company,
        ScopeLevel.Branch => ScopeTargetKind.Branch,
        ScopeLevel.Department => ScopeTargetKind.Department,
        ScopeLevel.SelectedEmployees => ScopeTargetKind.Employee,
        _ => null,
    };

    private static void Add(Dictionary<string, List<ScopeGrant>> result, string code, ScopeGrant grant)
    {
        if (!result.TryGetValue(code, out var list))
        {
            result[code] = list = [];
        }

        list.Add(grant);
    }
}
