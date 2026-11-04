using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

/// <summary>Employment-type values held on employment history / employees (stored as text).</summary>
public static class EmploymentTypes
{
    public const string Permanent = "permanent";
    public const string Probation = "probation";
    public const string Contract = "contract";
    public const string Intern = "intern";
    public const string Consultant = "consultant";

    public static readonly string[] All = [Permanent, Probation, Contract, Intern, Consultant];
}

/// <summary>Lifecycle event types (stored as text on lifecycle_events + employment_history.event_type).</summary>
public static class LifecycleEventTypes
{
    public const string Joining = "joining";
    public const string ProbationExtension = "probation_extension";
    public const string Confirmation = "confirmation";
    public const string Transfer = "transfer";
    public const string Promotion = "promotion";
    public const string ManagerChange = "manager_change";
    public const string BranchChange = "branch_change";
    public const string Rejoin = "rejoin";
    public const string Exit = "exit";
    public const string InitialAssignment = "initial_assignment";
    public const string Correction = "correction";

    public static readonly string[] All =
        [Joining, ProbationExtension, Confirmation, Transfer, Promotion, ManagerChange, BranchChange, Rejoin, Exit, InitialAssignment, Correction];
}

public enum LifecycleEventStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Effective = 4,
    Cancelled = 5,
}

/// <summary>Per-company (or tenant-default, company_id NULL) employee-code format config.</summary>
[Audited("core.employee")]
public sealed class EmployeeNumberingConfig : AuditableEntity, IConcurrencyAware
{
    public Guid? CompanyId { get; set; }
    public string Prefix { get; set; } = "EMP";
    public int PadWidth { get; set; } = 5;
    public bool AllowManual { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Atomic code counter — one row per series_key; NextValue is bumped with UPDATE…RETURNING.</summary>
public sealed class EmployeeCodeSeries
{
    public string SeriesKey { get; set; } = string.Empty;
    public long NextValue { get; set; } = 1;
}

/// <summary>
/// Effective-dated employment record — the single source of truth for an employee's org assignment/standing over time.
/// No-overlap is DB-enforced: EXCLUDE USING gist (employee_id WITH =, daterange(effective_from, effective_to) WITH &&).
/// The current row is the one with effective_to IS NULL. The employees table is a denormalized projection of it.
/// </summary>
[Audited("core.employee")]
public sealed class EmploymentHistory : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? DesignationId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? CostCentreId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public string EmploymentType { get; set; } = EmploymentTypes.Permanent;
    /// <summary>One of <see cref="LifecycleEventTypes"/> — which event opened this period.</summary>
    public string EventType { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>NULL = open (current) row.</summary>
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    /// <summary>True when this row was opened by a backdated/correction event (payroll-relevant).</summary>
    public bool PayrollAffecting { get; set; }
    public Guid? LifecycleEventId { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Approval-driven employment change (joining/transfer/promotion/exit/…). See design §5.</summary>
[Audited("core.employee")]
public sealed class LifecycleEvent : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    /// <summary>One of <see cref="LifecycleEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;
    public LifecycleEventStatus Status { get; set; } = LifecycleEventStatus.Draft;
    /// <summary>Event-specific payload (new org assignment, exit details, etc.) — jsonb.</summary>
    public string PayloadJson { get; set; } = "{}";
    public DateOnly EffectiveDate { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public string? Reason { get; set; }
    /// <summary>correction events link to the history row they replace.</summary>
    public Guid? CorrectsHistoryId { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public uint RowVersion { get; set; }
}
