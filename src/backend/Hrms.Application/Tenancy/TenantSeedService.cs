using Hrms.Application.Abstractions;
using Hrms.Application.Identity;
using Hrms.Application.Modules;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Tenancy;

public static class SystemTemplates
{
    public const string Invitation = "auth.invitation";
    public const string PasswordReset = "auth.password-reset";
    public const string EmailVerification = "auth.email-verification";

    public static IReadOnlyList<(string Code, string Subject, string Body)> All { get; } =
    [
        (Invitation, "You have been invited to {{ tenant_name }}",
            "<p>Hello {{ display_name }},</p><p>You have been invited to {{ tenant_name }} HRMS.</p><p><a href=\"{{ link }}\">Set your password</a> (valid for {{ hours }} hours).</p>"),
        (PasswordReset, "Reset your {{ tenant_name }} password",
            "<p>Hello {{ display_name }},</p><p><a href=\"{{ link }}\">Reset your password</a>. If you did not request this, ignore this email.</p>"),
        (EmailVerification, "Verify your email address",
            "<p>Hello {{ display_name }},</p><p><a href=\"{{ link }}\">Verify your email address</a>.</p>"),
    ];
}

/// <summary>Idempotent seeding of foundation data into a tenant database (permissions, system roles, templates, default dashboard).</summary>
public sealed class TenantSeedService(IModuleCatalog catalog)
{
    public async Task SeedAsync(ITenantDbContext db, CancellationToken ct)
    {
        await SyncPermissionsAsync(db, ct);

        if (!await db.AuthorizationState.AnyAsync(ct))
        {
            db.AuthorizationState.Add(new AuthorizationState { Id = 1, Version = 1 });
        }

        var permissions = await db.Permissions.Where(p => p.IsActive).ToListAsync(ct);
        await EnsureRoleAsync(db, SystemRoles.TenantAdmin, "Tenant Admin", permissions.Select(p => (p, ScopeLevel.Tenant)), ct);
        await EnsureRoleAsync(db, SystemRoles.Manager, "Manager",
            permissions.Where(p => p.Code is FoundationPermissions.DashboardView or FoundationPermissions.EmployeeView)
                .Select(p => (p, p.SupportsScope ? ScopeLevel.DirectReports : ScopeLevel.Tenant)), ct);
        await EnsureRoleAsync(db, SystemRoles.Employee, "Employee",
            permissions.Where(p => p.Code is FoundationPermissions.DashboardView or FoundationPermissions.EmployeeView)
                .Select(p => (p, p.SupportsScope ? ScopeLevel.Own : ScopeLevel.Tenant)), ct);

        foreach (var (code, subject, body) in SystemTemplates.All)
        {
            if (!await db.NotificationTemplates.AnyAsync(t => t.Code == code && t.Channel == NotificationChannels.Email, ct))
            {
                db.NotificationTemplates.Add(new NotificationTemplate { Code = code, Channel = NotificationChannels.Email, Subject = subject, Body = body, IsSystem = true });
            }
        }

        if (!await db.Dashboards.AnyAsync(d => d.Code == "default", ct))
        {
            var dashboard = new Dashboard { Code = "default", Name = "Home", IsSystem = true };
            dashboard.Widgets.Add(new DashboardWidget { DashboardId = dashboard.Id, WidgetCode = "core.welcome", X = 0, Y = 0, Width = 6, Height = 2 });
            dashboard.Widgets.Add(new DashboardWidget { DashboardId = dashboard.Id, WidgetCode = "core.notifications", X = 6, Y = 0, Width = 6, Height = 3 });
            db.Dashboards.Add(dashboard);
            db.DashboardAssignments.Add(new DashboardAssignment { DashboardId = dashboard.Id, AssigneeType = DashboardAssigneeType.TenantDefault });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SyncPermissionsAsync(ITenantDbContext db, CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var defined = catalog.AllPermissions.ToList();
        foreach (var d in defined)
        {
            if (!existing.TryGetValue(d.Code, out var p))
            {
                p = new Permission { Code = d.Code };
                db.Permissions.Add(p);
            }

            p.ModuleCode = d.ModuleCode;
            p.Kind = d.Kind;
            p.Action = d.Action;
            p.Description = d.Description;
            p.SupportsScope = d.SupportsScope;
            p.IsSensitive = d.IsSensitive;
            p.IsActive = true;
        }

        foreach (var stale in existing.Values.Where(p => defined.All(d => d.Code != p.Code)))
        {
            stale.IsActive = false;
        }

        await db.SaveChangesAsync(ct);

        // Tenant Admin always holds every active permission at tenant scope.
        var admin = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Code == SystemRoles.TenantAdmin, ct);
        if (admin is not null)
        {
            var all = await db.Permissions.Where(p => p.IsActive).Select(p => p.Id).ToListAsync(ct);
            foreach (var id in all.Where(id => admin.Permissions.All(rp => rp.PermissionId != id)))
            {
                admin.Permissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = id, ScopeLevel = ScopeLevel.Tenant });
            }

            var state = await db.AuthorizationState.FirstOrDefaultAsync(ct);
            if (state is not null)
            {
                state.Version++;
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureRoleAsync(ITenantDbContext db, string code, string name, IEnumerable<(Permission P, ScopeLevel Level)> perms, CancellationToken ct)
    {
        if (await db.Roles.AnyAsync(r => r.Code == code, ct))
        {
            return;
        }

        var role = new Role { Code = code, Name = name, IsSystem = true };
        role.Permissions.AddRange(perms.Select(x => new RolePermission { RoleId = role.Id, PermissionId = x.P.Id, ScopeLevel = x.Level }));
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<TenantUser> EnsureTenantAdminAsync(ITenantDbContext db, string email, string displayName, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            user = new TenantUser { Email = normalized, DisplayName = displayName, Status = UserStatus.Invited };
            db.Users.Add(user);
        }

        var role = await db.Roles.FirstAsync(r => r.Code == SystemRoles.TenantAdmin, ct);
        if (!await db.RoleAssignments.AnyAsync(a => a.UserId == user.Id && a.RoleId == role.Id, ct))
        {
            db.RoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = role.Id, ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow) });
        }

        await db.SaveChangesAsync(ct);
        return user;
    }
}
