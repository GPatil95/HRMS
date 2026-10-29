using Hrms.Domain.Common;
using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Platform;

[Audited("platform.identity")]
public sealed class PlatformUser : IdentityUserBase;

public sealed class PlatformSession : SessionBase;

public sealed class PlatformUserToken : UserTokenBase;

[Audited("platform.identity")]
public sealed class PlatformRole : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public List<PlatformRolePermission> Permissions { get; set; } = [];
}

public sealed class PlatformPermission : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class PlatformRolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public PlatformPermission? Permission { get; set; }
}

public sealed class PlatformUserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }
}

public enum SupportAccessLevel
{
    MetadataOnly = 0,
    ReadOnly = 1,
    ReadWrite = 2,
}

public enum SupportGrantStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Revoked = 3,
    Expired = 4,
}

[Audited("platform.support")]
public sealed class SupportAccessGrant : AuditableEntity
{
    public Guid TenantId { get; set; }
    public Guid PlatformUserId { get; set; }
    public string TicketRef { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public SupportAccessLevel AccessLevel { get; set; }
    public int DurationMinutes { get; set; }
    public SupportGrantStatus Status { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
