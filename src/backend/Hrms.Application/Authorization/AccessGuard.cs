using Hrms.Application.Abstractions;
using Hrms.Shared.Results;

namespace Hrms.Application.Authorization;

/// <summary>Imperative module + permission check for services whose required permission is data-driven (e.g. file purposes, reports).</summary>
public interface IAccessGuard
{
    Task<Error?> CheckAsync(string moduleCode, string permissionCode, CancellationToken ct);
}

public sealed class AccessGuard(ITenantContextAccessor tenant, ICurrentUser user, IEntitlementService entitlements, IPermissionService permissions) : IAccessGuard
{
    public async Task<Error?> CheckAsync(string moduleCode, string permissionCode, CancellationToken ct)
    {
        if (user.UserId is not { } userId || tenant.Current is null)
        {
            return Error.Unauthorized("unauthenticated", "Authentication required.");
        }

        var ent = await entitlements.GetAsync(tenant.Current.TenantId, ct);
        if (!ent.IsModuleEnabled(moduleCode))
        {
            return Error.Forbidden("module-not-enabled", "This module is not enabled for your organisation.");
        }

        return await permissions.HasAsync(userId, permissionCode, ct) ? null : Error.Forbidden("permission-denied", "You do not have permission to perform this action.");
    }
}
