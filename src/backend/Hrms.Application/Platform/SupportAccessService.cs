using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Platform;

public sealed record RequestSupportAccessCommand(Guid TenantId, string TicketRef, string Reason, SupportAccessLevel AccessLevel, int DurationMinutes);

public sealed record SupportGrantDto(Guid Id, Guid TenantId, Guid PlatformUserId, string TicketRef, string Reason, SupportAccessLevel AccessLevel,
    int DurationMinutes, SupportGrantStatus Status, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt);

/// <summary>
/// Explicit, reason-based, time-limited support access. Requester and approver must differ.
/// M1 provides the grant lifecycle and audit trail only; using a grant to enter tenant data arrives with the support console.
/// </summary>
public sealed class SupportAccessService(IPlatformDbContext db, ICurrentUser user, ISecurityEventWriter securityEvents, IClock clock)
{
    public const int MaxDurationMinutes = 240;

    public async Task<Result<Guid>> RequestAsync(RequestSupportAccessCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason) || string.IsNullOrWhiteSpace(cmd.TicketRef))
        {
            return Error.Validation("reason-required", "Ticket reference and reason are required.");
        }

        if (cmd.DurationMinutes is < 5 or > MaxDurationMinutes)
        {
            return Error.Validation("invalid-duration", $"Duration must be between 5 and {MaxDurationMinutes} minutes.");
        }

        if (!await db.Tenants.AnyAsync(t => t.Id == cmd.TenantId, ct))
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        var grant = new SupportAccessGrant
        {
            TenantId = cmd.TenantId,
            PlatformUserId = user.UserId!.Value,
            TicketRef = cmd.TicketRef.Trim(),
            Reason = cmd.Reason.Trim(),
            AccessLevel = cmd.AccessLevel,
            DurationMinutes = cmd.DurationMinutes,
            Status = SupportGrantStatus.Requested,
        };
        db.SupportGrants.Add(grant);
        securityEvents.Add(SecurityEventTypes.SupportAccessRequested, true, user.UserId, user.Email, new { tenantId = cmd.TenantId, grant = grant.Id, cmd.TicketRef, level = cmd.AccessLevel.ToString(), cmd.DurationMinutes });
        await db.SaveChangesAsync(ct);
        return grant.Id;
    }

    public async Task<Result> DecideAsync(Guid grantId, bool approve, CancellationToken ct)
    {
        var grant = await db.SupportGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null)
        {
            return Error.NotFound("grant-not-found", "Support grant not found.");
        }

        if (grant.Status != SupportGrantStatus.Requested)
        {
            return Error.Rule("grant-not-pending", "Support grant is not pending.");
        }

        if (grant.PlatformUserId == user.UserId)
        {
            return Error.Forbidden("self-approval", "Support access cannot be approved by the requester.");
        }

        var now = clock.UtcNow;
        grant.Status = approve ? SupportGrantStatus.Approved : SupportGrantStatus.Rejected;
        grant.DecidedBy = user.UserId;
        grant.DecidedAt = now;
        if (approve)
        {
            grant.StartsAt = now;
            grant.ExpiresAt = now.AddMinutes(grant.DurationMinutes);
        }

        securityEvents.Add(approve ? SecurityEventTypes.SupportAccessApproved : SecurityEventTypes.SupportAccessRevoked, true, grant.PlatformUserId, null,
            new { tenantId = grant.TenantId, grant = grant.Id, decision = approve ? "approved" : "rejected" });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RevokeAsync(Guid grantId, CancellationToken ct)
    {
        var grant = await db.SupportGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null)
        {
            return Error.NotFound("grant-not-found", "Support grant not found.");
        }

        if (grant.Status is not (SupportGrantStatus.Requested or SupportGrantStatus.Approved))
        {
            return Error.Rule("grant-not-active", "Support grant is not active.");
        }

        grant.Status = SupportGrantStatus.Revoked;
        grant.ExpiresAt = clock.UtcNow;
        securityEvents.Add(SecurityEventTypes.SupportAccessRevoked, true, grant.PlatformUserId, null, new { tenantId = grant.TenantId, grant = grant.Id });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<List<SupportGrantDto>> ListAsync(Guid? tenantId, CancellationToken ct) =>
        db.SupportGrants.AsNoTracking()
            .Where(g => tenantId == null || g.TenantId == tenantId)
            .OrderByDescending(g => g.CreatedAt).Take(200)
            .Select(g => new SupportGrantDto(g.Id, g.TenantId, g.PlatformUserId, g.TicketRef, g.Reason, g.AccessLevel, g.DurationMinutes, g.Status, g.StartsAt, g.ExpiresAt))
            .ToListAsync(ct);

    public Task<bool> HasActiveGrantAsync(Guid platformUserId, Guid tenantId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.SupportGrants.AnyAsync(g => g.PlatformUserId == platformUserId && g.TenantId == tenantId
            && g.Status == SupportGrantStatus.Approved && g.ExpiresAt > now, ct);
    }
}
