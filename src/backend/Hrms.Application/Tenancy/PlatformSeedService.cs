using Hrms.Application.Abstractions;
using Hrms.Application.Common;
using Hrms.Application.Identity;
using Hrms.Application.Modules;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Tenancy;

/// <summary>Idempotent platform seed: module catalogue, features, default plan, platform permissions/roles, default cluster.</summary>
public sealed class PlatformSeedService(IPlatformDbContext db, IModuleCatalog catalog, ISecretStore secrets)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var modules = await db.Modules.ToDictionaryAsync(m => m.Code, ct);
        foreach (var s in ModuleCatalogSeed.Modules)
        {
            if (!modules.TryGetValue(s.Code, out var m))
            {
                m = new CatalogModule { Code = s.Code };
                db.Modules.Add(m);
                modules[s.Code] = m;
            }

            m.Name = s.Name;
            m.Kind = s.Kind;
            m.ParentCode = s.ParentCode;
            m.SortOrder = s.SortOrder;
        }

        await db.SaveChangesAsync(ct);

        var deps = await db.ModuleDependencies.ToListAsync(ct);
        foreach (var s in ModuleCatalogSeed.Modules)
        {
            foreach (var req in s.Requires)
            {
                var mid = modules[s.Code].Id;
                var did = modules[req].Id;
                if (!deps.Any(d => d.ModuleId == mid && d.DependsOnModuleId == did))
                {
                    db.ModuleDependencies.Add(new ModuleDependency { ModuleId = mid, DependsOnModuleId = did, Kind = DependencyKind.Required });
                }
            }
        }

        var features = await db.Features.ToDictionaryAsync(f => f.Code, ct);
        foreach (var f in catalog.Modules.SelectMany(m => m.Features))
        {
            if (!features.ContainsKey(f.Code) && modules.TryGetValue(f.ModuleCode, out var mod))
            {
                db.Features.Add(new CatalogFeature { Code = f.Code, Name = f.Name, ModuleId = mod.Id, ValueType = f.ValueType });
            }
        }

        if (!await db.Plans.AnyAsync(p => p.Code == "foundation", ct))
        {
            var plan = new SubscriptionPlan { Code = "foundation", Name = "Foundation", Status = PlanStatus.Published };
            plan.Modules.AddRange(modules.Values.Where(m => m.Kind == ModuleKind.Core).Select(m => new PlanModule { PlanId = plan.Id, ModuleId = m.Id }));
            db.Plans.Add(plan);
        }

        var perms = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        foreach (var (code, description) in PlatformPermissions.All)
        {
            if (!perms.ContainsKey(code))
            {
                var p = new PlatformPermission { Code = code, Description = description };
                db.Permissions.Add(p);
                perms[code] = p;
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var r in PlatformManifest.Roles)
        {
            var role = await db.Roles.Include(x => x.Permissions).FirstOrDefaultAsync(x => x.Code == r.Code, ct);
            if (role is null)
            {
                role = new PlatformRole { Code = r.Code, Name = r.Name, IsSystem = true };
                db.Roles.Add(role);
            }

            foreach (var code in r.Permissions.Where(c => role.Permissions.All(rp => rp.PermissionId != perms[c].Id)))
            {
                role.Permissions.Add(new PlatformRolePermission { RoleId = role.Id, PermissionId = perms[code].Id });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> EnsureClusterAsync(string name, string host, int port, string adminUsername, string adminPassword, CancellationToken ct)
    {
        var cluster = await db.DbClusters.FirstOrDefaultAsync(c => c.Name == name, ct);
        var secretRef = secrets.BuildRef($"clusters/{name}/admin");
        await secrets.SetAsync(secretRef, adminPassword, ct);
        if (cluster is null)
        {
            cluster = new DbCluster { Name = name };
            db.DbClusters.Add(cluster);
        }

        cluster.Host = host;
        cluster.Port = port;
        cluster.AdminUsername = adminUsername;
        cluster.AdminSecretRef = secretRef;
        cluster.IsActive = true;
        await db.SaveChangesAsync(ct);
        return cluster.Id;
    }

    public async Task<Guid> EnsurePlatformAdminAsync(string email, string displayName, string password, CancellationToken ct)
    {
        var normalized = Guard.NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            user = new PlatformUser { Email = normalized, DisplayName = displayName };
            db.Users.Add(user);
        }

        user.PasswordHash = AuthService.HashPassword(password);
        user.Status = UserStatus.Active;
        user.EmailConfirmed = true;
        user.RotateSecurityStamp();
        var role = await db.Roles.FirstAsync(r => r.Code == PlatformManifest.SuperAdminRole, ct);
        if (!await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == role.Id, ct))
        {
            db.UserRoles.Add(new PlatformUserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync(ct);
        return user.Id;
    }
}
