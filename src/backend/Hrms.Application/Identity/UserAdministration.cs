using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Common;
using Hrms.Application.Modules;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hrms.Application.Identity;

public sealed record UserSummaryDto(Guid Id, string Email, string DisplayName, string Status, Guid? EmployeeId, bool MfaEnabled, DateTimeOffset? LastLoginAt);
public sealed record ScopeTargetDto(ScopeTargetKind Kind, Guid TargetId);
public sealed record RoleAssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<ScopeTargetDto> Targets);
public sealed record OverrideDto(Guid Id, string PermissionCode, string Effect, string? ScopeLevel, ScopeTargetKind? TargetKind, IReadOnlyList<Guid> TargetIds, DateOnly ValidFrom, DateOnly? ValidTo, string Reason);
public sealed record UserDetailDto(UserSummaryDto User, IReadOnlyList<RoleAssignmentDto> Roles, IReadOnlyList<OverrideDto> Overrides);
public sealed record CreateUserCommand(string Email, string DisplayName, Guid? EmployeeId, IReadOnlyList<Guid>? RoleIds);
public sealed record AssignRoleCommand(Guid RoleId, IReadOnlyList<ScopeTargetDto>? Targets, DateOnly? ValidFrom, DateOnly? ValidTo);
public sealed record AddOverrideCommand(string PermissionCode, PermissionEffect Effect, ScopeLevel? ScopeLevel, ScopeTargetKind? TargetKind, IReadOnlyList<Guid>? TargetIds, DateOnly? ValidFrom, DateOnly? ValidTo, string Reason);

public enum UserStatusAction { Activate, Deactivate, Lock, Unlock, Suspend }

public sealed class UserAdministrationService(
    ITenantDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IGrantCeilingGuard ceiling,
    ISecurityEventWriter securityEvents,
    IModuleCatalog catalog,
    IClock clock,
    [FromKeyedServices(Realms.Tenant)] AuthService auth)
{
    private static readonly Dictionary<string, System.Linq.Expressions.Expression<Func<TenantUser, object>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = u => u.Email, ["displayName"] = u => u.DisplayName, ["status"] = u => u.Status, ["createdAt"] = u => u.CreatedAt,
    };

    public async Task<PagedResult<UserSummaryDto>> ListAsync(PageRequest page, string? search, UserStatus? status, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Email.Contains(s) || u.DisplayName.ToLower().Contains(s));
        }

        if (status is not null)
        {
            q = q.Where(u => u.Status == status);
        }

        var total = await q.CountAsync(ct);
        var items = await q.ApplySort(page.Sort, SortMap, u => u.Email).Skip(page.Skip).Take(page.SafePageSize)
            .Select(u => new UserSummaryDto(u.Id, u.Email, u.DisplayName, u.Status.ToString(), u.EmployeeId, u.MfaEnabled, u.LastLoginAt))
            .ToListAsync(ct);
        return new PagedResult<UserSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<Result<UserDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
        {
            return Error.NotFound("user-not-found", "User not found.");
        }

        var roles = await db.RoleAssignments.AsNoTracking().Where(a => a.UserId == id)
            .Select(a => new RoleAssignmentDto(a.Id, a.RoleId, a.Role!.Code, a.Role.Name, a.ValidFrom, a.ValidTo,
                a.ScopeTargets.Select(t => new ScopeTargetDto(t.Kind, t.TargetId)).ToList()))
            .ToListAsync(ct);
        var overrides = await db.PermissionOverrides.AsNoTracking().Where(o => o.UserId == id)
            .Select(o => new OverrideDto(o.Id, o.Permission!.Code, o.Effect.ToString(), o.ScopeLevel == null ? null : o.ScopeLevel.ToString(), o.TargetKind, o.TargetIds, o.ValidFrom, o.ValidTo, o.Reason))
            .ToListAsync(ct);
        return new UserDetailDto(new UserSummaryDto(u.Id, u.Email, u.DisplayName, u.Status.ToString(), u.EmployeeId, u.MfaEnabled, u.LastLoginAt), roles, overrides);
    }

    public async Task<Result<Guid>> CreateAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        var email = Guard.NormalizeEmail(cmd.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Error.Conflict("user-exists", "A user with this email already exists.");
        }

        if (cmd.EmployeeId is { } empId)
        {
            if (!await db.Employees.AnyAsync(e => e.Id == empId, ct))
            {
                return Error.Validation("employee-not-found", "Employee not found.");
            }

            if (await db.Users.AnyAsync(u => u.EmployeeId == empId, ct))
            {
                return Error.Conflict("employee-already-linked", "This employee already has a user account.");
            }
        }

        var user = new TenantUser { Email = email, DisplayName = cmd.DisplayName.Trim(), EmployeeId = cmd.EmployeeId, Status = UserStatus.Invited };
        db.Users.Add(user);
        securityEvents.Add(SecurityEventTypes.UserCreated, true, user.Id, email);
        await db.SaveChangesAsync(ct);

        foreach (var roleId in cmd.RoleIds ?? [])
        {
            var r = await AssignRoleAsync(user.Id, new AssignRoleCommand(roleId, null, null, null), ct);
            if (!r.IsSuccess)
            {
                return r.Error!;
            }
        }

        await auth.SendInvitationAsync(user, ct);
        return user.Id;
    }

    public async Task<Result> ChangeStatusAsync(Guid userId, UserStatusAction action, string? reason, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Error.NotFound("user-not-found", "User not found.");
        }

        if (userId == currentUser.UserId)
        {
            return Error.Rule("cannot-change-own-status", "You cannot change the status of your own account.");
        }

        var (status, evt, revoke) = action switch
        {
            UserStatusAction.Activate => (UserStatus.Active, SecurityEventTypes.UserActivated, false),
            UserStatusAction.Deactivate => (UserStatus.Inactive, SecurityEventTypes.UserDeactivated, true),
            UserStatusAction.Lock => (UserStatus.Locked, SecurityEventTypes.UserLocked, true),
            UserStatusAction.Unlock => (UserStatus.Active, SecurityEventTypes.UserUnlocked, false),
            UserStatusAction.Suspend => (UserStatus.Suspended, SecurityEventTypes.UserSuspended, true),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (action == UserStatusAction.Activate && user.PasswordHash is null)
        {
            return Error.Rule("user-not-onboarded", "The user has not accepted the invitation yet.");
        }

        user.Status = status;
        if (action == UserStatusAction.Unlock)
        {
            user.LockoutEndUtc = null;
            user.AccessFailedCount = 0;
        }

        if (revoke)
        {
            user.RotateSecurityStamp();
        }

        securityEvents.Add(evt, true, user.Id, user.Email, new { reason });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (revoke)
        {
            await auth.RevokeAllSessionsAsync(user.Id, "status_" + status.ToString().ToLowerInvariant(), ct);
        }

        return Result.Success();
    }

    public async Task<Result> LinkEmployeeAsync(Guid userId, Guid? employeeId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Error.NotFound("user-not-found", "User not found.");
        }

        if (employeeId is { } id && (!await db.Employees.AnyAsync(e => e.Id == id, ct) || await db.Users.AnyAsync(u => u.EmployeeId == id && u.Id != userId, ct)))
        {
            return Error.Validation("employee-invalid", "Employee not found or already linked.");
        }

        user.EmployeeId = employeeId;
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> AssignRoleAsync(Guid userId, AssignRoleCommand cmd, CancellationToken ct)
    {
        var actor = currentUser.UserId!.Value;
        if (actor == userId)
        {
            return Error.Rule("cannot-change-own-roles", "You cannot change your own roles.");
        }

        var role = await db.Roles.AsNoTracking().Include(r => r.Permissions).ThenInclude(p => p.Permission).FirstOrDefaultAsync(r => r.Id == cmd.RoleId, ct);
        if (role is null || !await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return Error.NotFound("role-or-user-not-found", "Role or user not found.");
        }

        if (await db.RoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == cmd.RoleId, ct))
        {
            return Error.Conflict("role-already-assigned", "Role already assigned.");
        }

        var targets = cmd.Targets ?? [];
        foreach (var rp in role.Permissions)
        {
            var kind = PermissionService.TargetKindFor(rp.ScopeLevel);
            var ids = targets.Where(t => t.Kind == kind).Select(t => t.TargetId).ToList();
            var error = await ceiling.CheckGrantForUserAsync(actor, userId, rp.Permission!.Code, new ScopeGrant(rp.ScopeLevel, ids.Count > 0 ? kind : null, ids), ct);
            if (error is not null)
            {
                return error;
            }
        }

        var assignment = new UserRoleAssignment
        {
            UserId = userId,
            RoleId = cmd.RoleId,
            ValidFrom = cmd.ValidFrom ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            ValidTo = cmd.ValidTo,
        };
        assignment.ScopeTargets.AddRange(targets.Select(t => new AssignmentScopeTarget { AssignmentId = assignment.Id, Kind = t.Kind, TargetId = t.TargetId }));
        db.RoleAssignments.Add(assignment);
        securityEvents.Add(SecurityEventTypes.RoleAssigned, true, userId, null, new { roleId = role.Id, role = role.Code, targets });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return assignment.Id;
    }

    public async Task<Result> RemoveRoleAsync(Guid userId, Guid assignmentId, CancellationToken ct)
    {
        if (currentUser.UserId == userId)
        {
            return Error.Rule("cannot-change-own-roles", "You cannot change your own roles.");
        }

        var a = await db.RoleAssignments.Include(x => x.Role).Include(x => x.ScopeTargets).FirstOrDefaultAsync(x => x.Id == assignmentId && x.UserId == userId, ct);
        if (a is null)
        {
            return Error.NotFound("assignment-not-found", "Role assignment not found.");
        }

        if (a.Role!.Code == SystemRoles.TenantAdmin)
        {
            var admins = await db.RoleAssignments.CountAsync(x => x.RoleId == a.RoleId, ct);
            if (admins <= 1)
            {
                return Error.Rule("last-tenant-admin", "The last Tenant Admin cannot be removed.");
            }
        }

        db.AssignmentScopeTargets.RemoveRange(a.ScopeTargets);
        db.RoleAssignments.Remove(a);
        securityEvents.Add(SecurityEventTypes.RoleUnassigned, true, userId, null, new { role = a.Role.Code });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> AddOverrideAsync(Guid userId, AddOverrideCommand cmd, CancellationToken ct)
    {
        var actor = currentUser.UserId!.Value;
        if (actor == userId)
        {
            return Error.Rule("cannot-change-own-permissions", "You cannot change your own permissions.");
        }

        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Error.Validation("reason-required", "A reason is required for permission overrides.");
        }

        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == cmd.PermissionCode && p.IsActive, ct);
        if (permission is null || !await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return Error.NotFound("permission-or-user-not-found", "Permission or user not found.");
        }

        var definition = catalog.FindPermission(permission.Code);
        if (cmd.Effect == PermissionEffect.Grant)
        {
            var level = cmd.ScopeLevel ?? ScopeLevel.Tenant;
            var ids = cmd.TargetIds ?? [];
            var error = await ceiling.CheckGrantForUserAsync(actor, userId, permission.Code, new ScopeGrant(level, ids.Count > 0 ? cmd.TargetKind : null, ids), ct);
            if (error is not null)
            {
                return error;
            }

            if (definition?.IsSensitive == true && cmd.ValidTo is null)
            {
                return Error.Validation("expiry-required", "Grant overrides of sensitive permissions require an expiry date.");
            }
        }

        var o = new UserPermissionOverride
        {
            UserId = userId,
            PermissionId = permission.Id,
            Effect = cmd.Effect,
            ScopeLevel = cmd.Effect == PermissionEffect.Grant ? cmd.ScopeLevel ?? ScopeLevel.Tenant : null,
            TargetKind = cmd.TargetKind,
            TargetIds = (cmd.TargetIds ?? []).ToArray(),
            ValidFrom = cmd.ValidFrom ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            ValidTo = cmd.ValidTo,
            Reason = cmd.Reason.Trim(),
        };
        db.PermissionOverrides.Add(o);
        securityEvents.Add(SecurityEventTypes.PermissionOverrideChanged, true, userId, null, new { action = "add", permission = permission.Code, effect = cmd.Effect.ToString(), scope = cmd.ScopeLevel?.ToString(), cmd.Reason });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return o.Id;
    }

    public async Task<Result> RemoveOverrideAsync(Guid userId, Guid overrideId, CancellationToken ct)
    {
        if (currentUser.UserId == userId)
        {
            return Error.Rule("cannot-change-own-permissions", "You cannot change your own permissions.");
        }

        var o = await db.PermissionOverrides.Include(x => x.Permission).FirstOrDefaultAsync(x => x.Id == overrideId && x.UserId == userId, ct);
        if (o is null)
        {
            return Error.NotFound("override-not-found", "Override not found.");
        }

        db.PermissionOverrides.Remove(o);
        securityEvents.Add(SecurityEventTypes.PermissionOverrideChanged, true, userId, null, new { action = "remove", permission = o.Permission!.Code, effect = o.Effect.ToString() });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<int>> RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return Error.NotFound("user-not-found", "User not found.");
        }

        return await auth.RevokeAllSessionsAsync(userId, "admin_revoked", ct);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ScopeGrant>>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct)
    {
        var set = await permissions.GetEffectiveAsync(userId, ct);
        return set.Grants.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ScopeGrant>)kv.Value);
    }
}

public static class SystemRoles
{
    public const string TenantAdmin = "TENANT_ADMIN";
    public const string Manager = "MANAGER";
    public const string Employee = "EMPLOYEE";
}
