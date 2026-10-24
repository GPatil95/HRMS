using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Identity;

public sealed record RolePermissionDto(string PermissionCode, ScopeLevel ScopeLevel);
public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, int UserCount, IReadOnlyList<RolePermissionDto> Permissions);
public sealed record UpsertRoleCommand(string Code, string Name, string? Description, IReadOnlyList<RolePermissionDto> Permissions);
public sealed record PermissionDto(string Code, string ModuleCode, string Kind, string Action, string Description, bool SupportsScope, bool IsSensitive);

public sealed class RoleService(
    ITenantDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IGrantCeilingGuard ceiling,
    ISecurityEventWriter securityEvents)
{
    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.ModuleCode, p.Kind.ToString(), p.Action, p.Description, p.SupportsScope, p.IsSensitive))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Code, r.Name, r.Description, r.IsSystem,
                db.RoleAssignments.Count(a => a.RoleId == r.Id),
                r.Permissions.Select(p => new RolePermissionDto(p.Permission!.Code, p.ScopeLevel)).ToList()))
            .ToListAsync(ct);

    public async Task<Result<RoleDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var role = (await ListAsync(ct)).FirstOrDefault(r => r.Id == id);
        return role is null ? Error.NotFound("role-not-found", "Role not found.") : role;
    }

    public async Task<Result<Guid>> CreateAsync(UpsertRoleCommand cmd, CancellationToken ct)
    {
        var code = cmd.Code.Trim().ToUpperInvariant();
        if (await db.Roles.AnyAsync(r => r.Code == code, ct))
        {
            return Error.Conflict("role-exists", "A role with this code already exists.");
        }

        var role = new Role { Code = code, Name = cmd.Name.Trim(), Description = cmd.Description };
        var apply = await ApplyPermissionsAsync(role, cmd.Permissions, ct);
        if (apply is not null)
        {
            return apply;
        }

        db.Roles.Add(role);
        securityEvents.Add(SecurityEventTypes.RolePermissionsChanged, true, null, null, new { action = "create", role = code, permissions = cmd.Permissions });
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, UpsertRoleCommand cmd, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).ThenInclude(p => p.Permission).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return Error.NotFound("role-not-found", "Role not found.");
        }

        if (role.Code == SystemRoles.TenantAdmin)
        {
            return Error.Rule("system-role-locked", "The Tenant Admin role cannot be modified.");
        }

        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id && a.UserId == currentUser.UserId, ct))
        {
            return Error.Rule("cannot-edit-own-role", "You cannot edit a role assigned to yourself.");
        }

        var before = role.Permissions.Select(p => new RolePermissionDto(p.Permission!.Code, p.ScopeLevel)).ToList();
        role.Name = cmd.Name.Trim();
        role.Description = cmd.Description;
        var apply = await ApplyPermissionsAsync(role, cmd.Permissions, ct);
        if (apply is not null)
        {
            return apply;
        }

        securityEvents.Add(SecurityEventTypes.RolePermissionsChanged, true, null, null, new { action = "update", role = role.Code, before, after = cmd.Permissions });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return Error.NotFound("role-not-found", "Role not found.");
        }

        if (role.IsSystem)
        {
            return Error.Rule("system-role-locked", "System roles cannot be deleted.");
        }

        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id, ct))
        {
            return Error.Conflict("role-in-use", "Role is assigned to users.");
        }

        db.RolePermissions.RemoveRange(role.Permissions);
        db.Roles.Remove(role);
        securityEvents.Add(SecurityEventTypes.RolePermissionsChanged, true, null, null, new { action = "delete", role = role.Code });
        await permissions.BumpVersionAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Error?> ApplyPermissionsAsync(Role role, IReadOnlyList<RolePermissionDto> requested, CancellationToken ct)
    {
        var codes = requested.Select(r => r.PermissionCode).Distinct().ToList();
        var perms = await db.Permissions.Where(p => codes.Contains(p.Code) && p.IsActive).ToListAsync(ct);
        if (perms.Count != codes.Count)
        {
            return Error.Validation("unknown-permission", "One or more permissions do not exist.");
        }

        var existing = role.Permissions.ToDictionary(p => p.PermissionId);
        foreach (var r in requested)
        {
            var p = perms.First(x => x.Code == r.PermissionCode);
            var level = p.SupportsScope ? r.ScopeLevel : ScopeLevel.Tenant;
            var unchanged = existing.TryGetValue(p.Id, out var current) && current.ScopeLevel == level;
            if (!unchanged)
            {
                var error = await ceiling.CheckRolePermissionAsync(currentUser.UserId!.Value, p.Code, level, ct);
                if (error is not null)
                {
                    return error;
                }
            }
        }

        var keep = new HashSet<Guid>();
        foreach (var r in requested)
        {
            var p = perms.First(x => x.Code == r.PermissionCode);
            var level = p.SupportsScope ? r.ScopeLevel : ScopeLevel.Tenant;
            keep.Add(p.Id);
            if (existing.TryGetValue(p.Id, out var current))
            {
                current.ScopeLevel = level;
            }
            else
            {
                role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id, ScopeLevel = level });
            }
        }

        foreach (var removed in role.Permissions.Where(p => !keep.Contains(p.PermissionId)).ToList())
        {
            role.Permissions.Remove(removed);
            db.RolePermissions.Remove(removed);
        }

        return null;
    }
}
