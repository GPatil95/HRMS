using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Identity;
using Hrms.Application.Tenancy;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Hrms.Shared.Auditing;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hrms.Application.Platform;

public sealed record TenantSummaryDto(Guid Id, int TenantNumber, string Code, string Name, TenantStatus Status, string? PrimaryHost, DateTimeOffset CreatedAt);

public sealed record TenantModuleDto(string Code, string Name, ModuleKind Kind, bool Enabled, EntitlementSource? Source, DateOnly? ValidTo);

public sealed record TenantDetailDto(
    TenantSummaryDto Tenant,
    string? AdminEmail,
    string? AdminName,
    string? DatabaseName,
    TenantDatabaseStatus? DatabaseStatus,
    string? SchemaVersion,
    string? PlanCode,
    SubscriptionStatus? SubscriptionStatus,
    ProvisioningStatus? ProvisioningStatus,
    string? ProvisioningError,
    IReadOnlyList<TenantModuleDto> Modules);

/// <summary>Password null/empty → a tenant-realm password-reset email is sent to the admin;
/// otherwise the admin's password is set directly (hashed) and all sessions are revoked.</summary>
public sealed record ResetAdminPasswordCommand(string? Password);

public sealed record SetModuleEntitlementCommand(string ModuleCode, bool Enabled, string Reason, DateOnly? ValidTo);

public sealed record SetFeatureEntitlementCommand(string FeatureCode, bool Enabled, string? Value, string Reason);

public sealed record ChangeTenantStatusCommand(TenantStatus Status, string Reason);

public sealed record ChangeSubscriptionCommand(SubscriptionStatus Status, DateOnly? EndsOn, string Reason);

/// <summary>Platform-admin tenant lifecycle and entitlement management. Operates on Platform DB metadata only.</summary>
public sealed class TenantManagementService(
    IPlatformDbContext db,
    IEntitlementService entitlements,
    ITenantRegistry registry,
    IAuditWriter audit,
    ISecurityEventWriter securityEvents,
    IClock clock,
    IServiceScopeFactory scopes,
    IRequestContext request,
    IPasswordPolicy passwordPolicy)
{
    public async Task<PagedResult<TenantSummaryDto>> ListAsync(PageRequest page, string? search, CancellationToken ct)
    {
        var q = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(t => t.Code.Contains(s) || t.Name.ToLower().Contains(s));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(t => t.TenantNumber).Skip(page.Skip).Take(page.SafePageSize)
            .Select(t => new TenantSummaryDto(t.Id, t.TenantNumber, t.Code, t.Name, t.Status,
                t.Domains.Where(d => d.IsPrimary).Select(d => d.HostName).FirstOrDefault(), t.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<TenantSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<Result<TenantDetailDto>> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().Include(x => x.Domains).FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null)
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        var tdb = await db.TenantDatabases.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var sub = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartsOn).FirstOrDefaultAsync(ct);
        var plan = sub is null ? null : await db.Plans.AsNoTracking().Where(p => p.Id == sub.PlanId).Select(p => p.Code).FirstOrDefaultAsync(ct);
        var op = await db.ProvisioningOperations.AsNoTracking().Where(o => o.TenantId == tenantId).OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        var modules = await db.Modules.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToListAsync(ct);
        var ents = await db.ModuleEntitlements.AsNoTracking().Where(e => e.TenantId == tenantId).ToListAsync(ct);
        var effective = await entitlements.GetAsync(tenantId, ct);
        var moduleDtos = modules.Select(m =>
        {
            var e = ents.FirstOrDefault(x => x.ModuleId == m.Id);
            return new TenantModuleDto(m.Code, m.Name, m.Kind, effective.IsModuleEnabled(m.Code), e?.Source, e?.ValidTo);
        }).ToList();
        var summary = new TenantSummaryDto(t.Id, t.TenantNumber, t.Code, t.Name, t.Status, t.Domains.FirstOrDefault(d => d.IsPrimary)?.HostName, t.CreatedAt);
        return new TenantDetailDto(summary, t.AdminEmail, t.AdminName, tdb?.DatabaseName, tdb?.Status, tdb?.SchemaVersion, plan, sub?.Status, op?.Status, op?.Error, moduleDtos);
    }

    /// <summary>Platform-admin reset of the tenant admin's credentials. Runs inside the tenant's own context/database:
    /// with a password → hashes it via the tenant auth mechanism and revokes all sessions; without → sends the
    /// tenant password-reset email. The admin username is the tenant admin email (login identifier).</summary>
    public async Task<Result> ResetAdminPasswordAsync(Guid tenantId, string? newPassword, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null)
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        if (!await db.TenantDatabases.AnyAsync(d => d.TenantId == tenantId && d.Status == TenantDatabaseStatus.Online, ct))
        {
            return Error.Rule("tenant-db-not-online", "Tenant database is not online yet.");
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            var errors = passwordPolicy.Validate(newPassword, t.AdminEmail);
            if (errors.Count > 0)
            {
                return Error.Validation("password-policy", string.Join(" ", errors));
            }
        }

        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ExecutionContextState>().SetRequest(request.CorrelationId, null, null, AuditSources.System);
        var tenant = await sp.GetRequiredService<ITenantRegistry>().FindByIdAsync(tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        sp.GetRequiredService<ITenantContextAccessor>().Set(tenant);
        await sp.GetRequiredService<ITenantDatabaseResolver>().EnsureResolvedAsync(tenant, ct);
        var tdb = sp.GetRequiredService<ITenantDbContext>();
        var adminEmail = t.AdminEmail.Trim().ToLowerInvariant();
        var admin = await tdb.Users.FirstOrDefaultAsync(u => u.Email == adminEmail, ct);
        if (admin is null)
        {
            return Error.NotFound("admin-not-found", "Tenant admin user not found.");
        }

        var auth = sp.GetRequiredKeyedService<AuthService>(Realms.Tenant);
        var tenantEvents = sp.GetRequiredService<ISecurityEventWriter>();
        if (string.IsNullOrEmpty(newPassword))
        {
            await auth.ForgotPasswordAsync(adminEmail, ct);
            securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null,
                new { action = "admin_password_reset_email", tenant = t.Code, admin = adminEmail });
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }

        admin.PasswordHash = new PasswordHasher<IdentityUserBase>().HashPassword(admin, newPassword);
        admin.PasswordChangedAt = clock.UtcNow;
        admin.LockoutEndUtc = null;
        admin.AccessFailedCount = 0;
        admin.RotateSecurityStamp();
        if (admin.Status is UserStatus.Invited)
        {
            admin.Status = UserStatus.Active;
            admin.EmailConfirmed = true;
        }

        tenantEvents.Add(SecurityEventTypes.PasswordResetCompleted, true, admin.Id, admin.Email, new { via = "platform_admin" });
        await tdb.SaveChangesAsync(ct);
        await auth.RevokeAllSessionsAsync(admin.Id, "platform_admin_reset", ct);
        securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null,
            new { action = "admin_password_reset", tenant = t.Code, admin = adminEmail });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(Guid tenantId, ChangeTenantStatusCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Error.Validation("reason-required", "A reason is required.");
        }

        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null)
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        if (cmd.Status is TenantStatus.Provisioning or TenantStatus.ProvisioningFailed || !t.CanTransitionTo(cmd.Status))
        {
            return Error.Rule("invalid-status-transition", $"Cannot change tenant status from {t.Status} to {cmd.Status}.");
        }

        if (cmd.Status == TenantStatus.Active && !await db.TenantDatabases.AnyAsync(d => d.TenantId == tenantId && d.Status == TenantDatabaseStatus.Online, ct))
        {
            return Error.Rule("tenant-db-not-online", "Tenant database is not online.");
        }

        var previous = t.Status;
        t.Status = cmd.Status;
        securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null, new { action = "status", tenant = t.Code, from = previous.ToString(), to = cmd.Status.ToString(), reason = cmd.Reason });
        await db.SaveChangesAsync(ct);
        await registry.InvalidateAsync(tenantId, ct);
        return Result.Success();
    }

    public async Task<Result> SetModuleEntitlementAsync(Guid tenantId, SetModuleEntitlementCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Error.Validation("reason-required", "A reason is required.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var module = await db.Modules.FirstOrDefaultAsync(m => m.Code == cmd.ModuleCode, ct);
        if (tenant is null || module is null)
        {
            return Error.NotFound("not-found", "Tenant or module not found.");
        }

        if (module.Kind == ModuleKind.Core)
        {
            return Error.Rule("core-module", "Core modules are always enabled.");
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (cmd.Enabled)
        {
            // Hard dependencies must be enabled first (e.g. payroll.tax requires payroll).
            var required = await (from d in db.ModuleDependencies
                                  join m in db.Modules on d.DependsOnModuleId equals m.Id
                                  where d.ModuleId == module.Id && d.Kind == DependencyKind.Required && m.Kind != ModuleKind.Core
                                  select m.Code).ToListAsync(ct);
            var current = await entitlements.GetAsync(tenantId, ct);
            var missing = required.Where(r => !current.Modules.Contains(r)).ToList();
            if (missing.Count > 0)
            {
                return Error.Rule("module-dependency-missing", $"Enable required modules first: {string.Join(", ", missing)}.");
            }
        }
        else
        {
            var dependents = await (from d in db.ModuleDependencies
                                    join m in db.Modules on d.ModuleId equals m.Id
                                    join e in db.ModuleEntitlements on m.Id equals e.ModuleId
                                    where d.DependsOnModuleId == module.Id && d.Kind == DependencyKind.Required && e.TenantId == tenantId && e.IsEnabled
                                    select m.Code).ToListAsync(ct);
            if (dependents.Count > 0)
            {
                return Error.Rule("module-has-dependents", $"Disable dependent modules first: {string.Join(", ", dependents)}.");
            }
        }

        var ent = await db.ModuleEntitlements.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.ModuleId == module.Id, ct);
        if (ent is null)
        {
            ent = new TenantModuleEntitlement { TenantId = tenantId, ModuleId = module.Id, Source = EntitlementSource.Manual, ValidFrom = today };
            db.ModuleEntitlements.Add(ent);
        }

        ent.IsEnabled = cmd.Enabled;
        ent.ValidTo = cmd.ValidTo;
        ent.Reason = cmd.Reason;
        tenant.EntitlementVersion++;
        securityEvents.Add(SecurityEventTypes.SubscriptionChanged, true, null, null, new { tenant = tenant.Code, module = module.Code, enabled = cmd.Enabled, reason = cmd.Reason });
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(tenantId, ct);
        await registry.InvalidateAsync(tenantId, ct);
        return Result.Success();
    }

    public async Task<Result> SetFeatureEntitlementAsync(Guid tenantId, SetFeatureEntitlementCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Error.Validation("reason-required", "A reason is required.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Code == cmd.FeatureCode, ct);
        if (tenant is null || feature is null)
        {
            return Error.NotFound("not-found", "Tenant or feature not found.");
        }

        var ent = await db.FeatureEntitlements.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.FeatureId == feature.Id, ct);
        if (ent is null)
        {
            ent = new TenantFeatureEntitlement { TenantId = tenantId, FeatureId = feature.Id, Source = EntitlementSource.Manual, ValidFrom = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime) };
            db.FeatureEntitlements.Add(ent);
        }

        ent.IsEnabled = cmd.Enabled;
        ent.Value = cmd.Value;
        tenant.EntitlementVersion++;
        securityEvents.Add(SecurityEventTypes.SubscriptionChanged, true, null, null, new { tenant = tenant.Code, feature = feature.Code, enabled = cmd.Enabled, reason = cmd.Reason });
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(tenantId, ct);
        await registry.InvalidateAsync(tenantId, ct);
        return Result.Success();
    }

    public async Task<Result> ChangeSubscriptionAsync(Guid tenantId, ChangeSubscriptionCommand cmd, CancellationToken ct)
    {
        var sub = await db.Subscriptions.Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartsOn).FirstOrDefaultAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (sub is null || tenant is null)
        {
            return Error.NotFound("subscription-not-found", "Subscription not found.");
        }

        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Error.Validation("reason-required", "A reason is required.");
        }

        var from = sub.Status;
        sub.Status = cmd.Status;
        sub.EndsOn = cmd.EndsOn;
        tenant.EntitlementVersion++;
        securityEvents.Add(SecurityEventTypes.SubscriptionChanged, true, null, null, new { tenant = tenant.Code, from = from.ToString(), to = cmd.Status.ToString(), reason = cmd.Reason });
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(tenantId, ct);
        await registry.InvalidateAsync(tenantId, ct);
        return Result.Success();
    }

    public Task<List<CatalogModule>> ListCatalogAsync(CancellationToken ct) =>
        db.Modules.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);

    public Task<List<SubscriptionPlan>> ListPlansAsync(CancellationToken ct) =>
        db.Plans.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

    public Task<List<SecurityEvent>> RecentSecurityEventsAsync(int take, CancellationToken ct) =>
        db.SecurityEvents.AsNoTracking().OrderByDescending(e => e.OccurredAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);

    public Task<List<AuditEntry>> RecentAuditAsync(int take, CancellationToken ct) =>
        db.AuditEntries.AsNoTracking().OrderByDescending(e => e.OccurredAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);

    internal IAuditWriter Audit => audit;
}
