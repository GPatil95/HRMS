using Hrms.Application.Abstractions;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Workflow;

/// <summary>Database-only effective-permission checks (role grants + grant overrides − deny overrides), usable inside
/// the retrying workflow transaction where <c>IPermissionService</c> (HybridCache) cannot run.</summary>
internal static class WorkflowEligibility
{
    /// <summary>Users who can both open My Work and act on approvals.</summary>
    public static IQueryable<Guid> UsersWhoCanAct(ITenantDbContext db, DateOnly today)
    {
        var viewers = UsersWithPermission(db, Modules.WorkflowPermissions.WorkItemsView, today);
        return UsersWithPermission(db, Modules.WorkflowPermissions.WorkItemsAct, today).Where(u => viewers.Contains(u));
    }

    public static IQueryable<Guid> UsersWithPermission(ITenantDbContext db, string permissionCode, DateOnly today, bool tenantWideOnly = false)
    {
        var overrides = db.PermissionOverrides.AsNoTracking()
            .Where(o => o.ValidFrom <= today && (o.ValidTo == null || o.ValidTo >= today) && o.Permission!.Code == permissionCode && o.Permission.IsActive);
        var denied = overrides.Where(o => o.Effect == PermissionEffect.Deny).Select(o => o.UserId);
        var granted = overrides.Where(o => o.Effect == PermissionEffect.Grant && (!tenantWideOnly || o.ScopeLevel == null || o.ScopeLevel == ScopeLevel.Tenant))
            .Select(o => o.UserId);
        var viaRoles = db.RoleAssignments.AsNoTracking()
            .Where(a => a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today)
                        && a.Role!.Permissions.Any(p => p.Permission!.Code == permissionCode && p.Permission.IsActive
                                                        && (!tenantWideOnly || p.ScopeLevel == ScopeLevel.Tenant)))
            .Select(a => a.UserId);
        return db.Users.AsNoTracking()
            .Where(u => (viaRoles.Contains(u.Id) || granted.Contains(u.Id)) && !denied.Contains(u.Id))
            .Select(u => u.Id);
    }
}
