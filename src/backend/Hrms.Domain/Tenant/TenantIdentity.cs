using Hrms.Domain.Common;
using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;
using Hrms.Shared.Security;

namespace Hrms.Domain.Tenant;

/// <summary>Single row identifying which tenant owns this database. Verified on first use of every connection string.</summary>
public sealed class TenantIdentityRecord
{
    public Guid TenantId { get; set; }
    public int TenantNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[Audited("core.identity")]
public sealed class TenantUser : IdentityUserBase
{
    public Guid? EmployeeId { get; set; }
    public List<UserRoleAssignment> RoleAssignments { get; set; } = [];
}

public sealed class TenantSession : SessionBase;

public sealed class TenantUserToken : UserTokenBase;

[Audited("core.identity")]
public sealed class Permission : Entity
{
    public string Code { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public PermissionKind Kind { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsScope { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsActive { get; set; } = true;
}

[Audited("core.identity")]
public sealed class Role : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public uint RowVersion { get; set; }
    public List<RolePermission> Permissions { get; set; } = [];
}

[Audited("core.identity")]
public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public ScopeLevel ScopeLevel { get; set; } = ScopeLevel.Tenant;
    public Permission? Permission { get; set; }
}

public enum ScopeTargetKind
{
    Company = 1,
    Branch = 2,
    Department = 3,
    Employee = 4,
}

[Audited("core.identity")]
public sealed class UserRoleAssignment : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public Role? Role { get; set; }
    public List<AssignmentScopeTarget> ScopeTargets { get; set; } = [];
}

public sealed class AssignmentScopeTarget : Entity
{
    public Guid AssignmentId { get; set; }
    public ScopeTargetKind Kind { get; set; }
    public Guid TargetId { get; set; }
}

[Audited("core.identity")]
public sealed class UserPermissionOverride : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public PermissionEffect Effect { get; set; }
    public ScopeLevel? ScopeLevel { get; set; }
    public ScopeTargetKind? TargetKind { get; set; }
    public Guid[] TargetIds { get; set; } = [];
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Permission? Permission { get; set; }
}

/// <summary>Tenant-wide authorization version, bumped on any role/permission/assignment/override change (cache key).</summary>
public sealed class AuthorizationState
{
    public int Id { get; set; } = 1;
    public long Version { get; set; } = 1;
}
