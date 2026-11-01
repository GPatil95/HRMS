using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

public static class SalaryComponentTypes
{
    public const string Earning = "earning";
    public const string Deduction = "deduction";
    public const string Benefit = "benefit";
    public const string Statutory = "statutory";
    public const string Reimbursement = "reimbursement";

    public static readonly string[] All = [Earning, Deduction, Benefit, Statutory, Reimbursement];
}

public static class SalaryCalcTypes
{
    public const string Fixed = "fixed";
    public const string PercentOfBasic = "percent_of_basic";
    public const string PercentOfGross = "percent_of_gross";
    public const string Formula = "formula";

    public static readonly string[] All = [Fixed, PercentOfBasic, PercentOfGross, Formula];
}

public static class SalaryAssignmentStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";   // approved, awaiting effective date
    public const string Active = "active";       // the open (current) assignment
    public const string Closed = "closed";       // superseded by a newer active assignment
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>Reusable pay element: earning, deduction, benefit, statutory or reimbursement.</summary>
[Audited("hr.salary")]
public sealed class SalaryComponent : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = SalaryComponentTypes.Earning;
    /// <summary>Name printed on payslips; defaults to Name.</summary>
    public string? PayslipName { get; set; }
    public bool Taxable { get; set; } = true;
    /// <summary>Prorated by attendance days in payroll.</summary>
    public bool ProRata { get; set; } = true;
    /// <summary>Counts toward annual CTC.</summary>
    public bool PartOfCtc { get; set; } = true;
    /// <summary>Counts toward gross pay.</summary>
    public bool PartOfGross { get; set; } = true;
    /// <summary>System-managed statutory element (PF, ESI, PT, TDS) — cannot be deleted.</summary>
    public bool IsStatutory { get; set; }
    /// <summary>Marks the component other percent_of_basic calculations resolve against.</summary>
    public bool IsBasic { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Named pay package versioned by effective date; a tenant can keep several structures.</summary>
[Audited("hr.salary")]
public sealed class SalaryStructure : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
    public List<SalaryStructureLine> Lines { get; set; } = [];
}

/// <summary>One component inside a structure, with how its amount is computed.</summary>
[Audited("hr.salary")]
public sealed class SalaryStructureLine : Entity
{
    public Guid SalaryStructureId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public int Sequence { get; set; }
    public string CalcType { get; set; } = SalaryCalcTypes.Fixed;
    /// <summary>Monthly amount when CalcType = fixed.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Percentage when CalcType = percent_of_basic / percent_of_gross.</summary>
    public decimal? Percent { get; set; }
    /// <summary>Free-form formula (payroll engine evaluates; HR-side stores verbatim).</summary>
    public string? Formula { get; set; }
}

/// <summary>
/// Effective-dated salary assignment: one row per period. The open row (effective_to IS NULL)
/// is the current salary; the daterange exclusion constraint prevents overlaps.
/// Draft → submitted → approved →(on effective date, via daily sweep)→ active.
/// </summary>
[Audited("hr.salary")]
public sealed class EmployeeSalaryAssignment : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Guid SalaryStructureId { get; set; }
    /// <summary>Annual cost to company in minor-free decimal currency units.</summary>
    public decimal CtcAmount { get; set; }
    public string? Currency { get; set; } = "INR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = SalaryAssignmentStatuses.Draft;
    public string? Reason { get; set; }
    public Guid? LifecycleEventId { get; set; }
    public Guid? RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public uint RowVersion { get; set; }
    public List<EmployeeSalaryLine> Lines { get; set; } = [];
}

/// <summary>Resolved component amounts captured at approval time — the payroll-readiness snapshot.
/// resolved_amount is filled for fixed/percent types the backend can compute now; formula lines
/// carry the expression through for the payroll engine.</summary>
public sealed class EmployeeSalaryLine : Entity
{
    public Guid EmployeeSalaryAssignmentId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public int Sequence { get; set; }
    public string CalcType { get; set; } = SalaryCalcTypes.Fixed;
    public decimal? Amount { get; set; }
    public decimal? Percent { get; set; }
    public string? Formula { get; set; }
    /// <summary>Monthly resolved amount where computable (fixed, percent_of_basic on a resolvable basic).</summary>
    public decimal? ResolvedAmount { get; set; }
}
