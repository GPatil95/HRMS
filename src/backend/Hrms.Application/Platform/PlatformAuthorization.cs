using Hrms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Platform;

/// <summary>Platform-realm permission checks. Platform permissions never grant tenant business data access.</summary>
public interface IPlatformPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid platformUserId, CancellationToken ct);
    Task<bool> HasAsync(Guid platformUserId, string permissionCode, CancellationToken ct);
}

public sealed class PlatformPermissionService(IPlatformDbContext db) : IPlatformPermissionService
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid platformUserId, CancellationToken ct)
    {
        var codes = await (
            from ur in db.UserRoles
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == platformUserId
            select p.Code).Distinct().ToListAsync(ct);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> HasAsync(Guid platformUserId, string permissionCode, CancellationToken ct) =>
        (await GetPermissionsAsync(platformUserId, ct)).Contains(permissionCode);
}
