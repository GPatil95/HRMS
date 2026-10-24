using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

[Audited("core.org")]
public sealed class Company : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

[Audited("core.org")]
public sealed class Branch : AuditableEntity, IConcurrencyAware
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

[Audited("core.org")]
public sealed class Department : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

public enum EmployeeStatus
{
    PreJoining = 0,
    Active = 1,
    Notice = 2,
    Exited = 3,
}

/// <summary>Minimal employee directory record used by data scopes and user linking. The full Employee module comes later.</summary>
[Audited("core.employee")]
public sealed class Employee : AuditableEntity, IConcurrencyAware
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? WorkEmail { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public uint RowVersion { get; set; }
}

/// <summary>Reporting hierarchy closure table (ancestor, descendant, depth). Depth 0 = self.</summary>
public sealed class ReportingClosure
{
    public Guid AncestorId { get; set; }
    public Guid DescendantId { get; set; }
    public int Depth { get; set; }
}
