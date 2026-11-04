using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Domain.Common;
using Hrms.Shared.Results;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Authorization;

/// <summary>Enforces "a user can never grant more than they hold" (architecture §10.5).</summary>
public interface IGrantCeilingGuard
{
    Task<Error?> CheckRolePermissionAsync(Guid actorUserId, string permissionCode, ScopeLevel level, CancellationToken ct);
    Task<Error?> CheckGrantForUserAsync(Guid actorUserId, Guid targetUserId, string permissionCode, ScopeGrant requested, CancellationToken ct);
}

public sealed class GrantCeilingGuard(
    IPermissionService permissions,
    IDataScopeService scopes,
    ITenantDbContext db,
    ISecurityEventWriter securityEvents) : IGrantCeilingGuard
{
    public static Error Violation(string code) =>
        Error.Forbidden("grant-exceeds-own-permission", $"You cannot grant '{code}' beyond the access you hold yourself.");

    public async Task<Error?> CheckRolePermissionAsync(Guid actorUserId, string permissionCode, ScopeLevel level, CancellationToken ct)
    {
        var actor = (await permissions.GetEffectiveAsync(actorUserId, ct)).ScopesFor(permissionCode);
        if (actor.Any(a => Covers(a.Level, level) && a.TargetIds.Count == 0))
        {
            return null;
        }

        return await RejectAsync(actorUserId, permissionCode, level.ToString(), ct);
    }

    public async Task<Error?> CheckGrantForUserAsync(Guid actorUserId, Guid targetUserId, string permissionCode, ScopeGrant requested, CancellationToken ct)
    {
        var actorGrants = (await permissions.GetEffectiveAsync(actorUserId, ct)).ScopesFor(permissionCode);
        if (actorGrants.Count == 0)
        {
            return await RejectAsync(actorUserId, permissionCode, requested.Level.ToString(), ct);
        }

        if (actorGrants.Any(a => a.Level == ScopeLevel.Tenant))
        {
            return null;
        }

        if (requested.Level == ScopeLevel.Tenant)
        {
            return await RejectAsync(actorUserId, permissionCode, requested.Level.ToString(), ct);
        }

        // Compare the resolved employee sets: requested (in the context of the target user) must be a subset of the actor's.
        var actorScope = await scopes.ResolveForGrantsAsync(actorUserId, actorGrants, ct);
        var requestedScope = await scopes.ResolveForGrantsAsync(targetUserId, [requested], ct);
        var requestedIds = await scopes.ApplyScope(db.Employees.AsNoTracking(), requestedScope).Select(e => e.Id).ToListAsync(ct);
        var actorIds = (await scopes.ApplyScope(db.Employees.AsNoTracking(), actorScope).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        if (requestedIds.All(actorIds.Contains) && !(requestedScope.None && requested.Level != ScopeLevel.Own && requested.TargetIds.Count > 0))
        {
            return null;
        }

        return await RejectAsync(actorUserId, permissionCode, requested.Level.ToString(), ct);
    }

    /// <summary>Role-level comparison (no assignee context): same chain and wider or equal, Own is covered by any level.</summary>
    public static bool Covers(ScopeLevel actor, ScopeLevel requested)
    {
        if (actor == ScopeLevel.Tenant || requested == ScopeLevel.Own)
        {
            return true;
        }

        static int Chain(ScopeLevel l) => l switch
        {
            ScopeLevel.Own or ScopeLevel.DirectReports or ScopeLevel.AllReports => 1,
            ScopeLevel.Department or ScopeLevel.Branch or ScopeLevel.Company => 2,
            ScopeLevel.SelectedEmployees => 3,
            _ => 4,
        };

        return Chain(actor) == Chain(requested) && (int)actor >= (int)requested;
    }

    private async Task<Error> RejectAsync(Guid actor, string code, string level, CancellationToken ct)
    {
        securityEvents.Add(SecurityEventTypes.GrantCeilingViolation, false, actor, null, new { permission = code, level });
        await db.SaveChangesAsync(ct);
        return Violation(code);
    }
}
