using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

[Audited("core.org")]
public sealed class Company : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? CompanyType { get; set; }
    public string? Industry { get; set; }
    public Guid? ParentCompanyId { get; set; }
    public DateOnly? IncorporationDate { get; set; }
    public string? Website { get; set; }
    public string? OfficialEmail { get; set; }
    public string? ContactNumber { get; set; }
    public Guid? LogoFileId { get; set; }

    // Statutory registrations (single-value; multi-GSTIN lives in OrgRegistrations)
    public string? Cin { get; set; }
    public string? Pan { get; set; }
    public string? Tan { get; set; }
    public string? PfEstablishmentCode { get; set; }
    public string? EsicEmployerCode { get; set; }
    public string? ProfessionalTaxRegNo { get; set; }
    public string? ShopsEstablishmentRegNo { get; set; }
    public string? FactoryLicenceNo { get; set; }
    public string? MsmeUdyamRegNo { get; set; }

    // Finance & payroll defaults
    public string? Currency { get; set; }
    public int? FinancialYearStartMonth { get; set; }
    public string? PayrollFrequency { get; set; }
    public int? SalaryPayDay { get; set; }
    public string? DefaultLeavePolicyCode { get; set; }
    public string? DefaultHolidayCalendarCode { get; set; }
    public string? DefaultAttendancePolicyCode { get; set; }

    // Company-level work-schedule defaults (overridable at branch/department)
    public string? DefaultShiftName { get; set; }
    public string? WeeklyOffPattern { get; set; }
    public string? WeeklyOffConfigJson { get; set; }
    public int? GraceMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>A company address (registered / corporate / mailing / billing).</summary>
[Audited("core.org")]
public sealed class CompanyAddress : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string AddressType { get; set; } = string.Empty;
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? Landmark { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? PinCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

/// <summary>A company or branch statutory registration row (GSTIN or any other type).</summary>
[Audited("core.org")]
public sealed class OrgRegistration : AuditableEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string RegistrationType { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? Jurisdiction { get; set; }
    public string? RegistrationSubtype { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Guid? DocumentFileId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A company contact (signatory / hr / payroll / finance / other).</summary>
[Audited("core.org")]
public sealed class CompanyContact : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string ContactType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>A company bank account used for salary / invoicing. Access is permission-gated by org.company.edit.</summary>
[Audited("core.org")]
public sealed class CompanyBankAccount : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string? Ifsc { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>HOD / approver assignment history on a department.</summary>
[Audited("core.org")]
public sealed class DepartmentHodHistory : AuditableEntity
{
    public Guid DepartmentId { get; set; }
    public string Role { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
}

[Audited("core.org")]
public sealed class Branch : AuditableEntity, IConcurrencyAware
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string? BranchType { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public Guid? HrEmployeeId { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PinCode { get; set; }
    public string? WorkLocationType { get; set; }
    public string? ClientSiteRef { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? GeofenceRadiusMeters { get; set; }

    // Work-schedule & attendance config (override of company defaults when set)
    public string? DefaultShiftName { get; set; }
    public TimeOnly? WorkStart { get; set; }
    public TimeOnly? WorkEnd { get; set; }
    public int? BreakMinutes { get; set; }
    public string? WeeklyOffPattern { get; set; }
    public string? WeeklyOffConfigJson { get; set; }
    public string? HolidayCalendarCode { get; set; }
    public int? GraceMinutes { get; set; }
    public bool? OvertimeAllowed { get; set; }
    public string? AttendanceMode { get; set; }
    public string? AttendancePolicyCode { get; set; }

    // Payroll & billing config
    public int? PayrollDay { get; set; }
    public string? CostCentre { get; set; }

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
    public string? DepartmentType { get; set; }
    public string? Description { get; set; }
    public string? CostCentre { get; set; }
    public DateOnly? EffectiveDate { get; set; }

    // HOD & hierarchy (HOD must reference a same-tenant employee)
    public Guid? HodEmployeeId { get; set; }
    public Guid? AltHodEmployeeId { get; set; }
    public Guid? ReportingManagerEmployeeId { get; set; }
    public DateOnly? HodEffectiveFrom { get; set; }

    // Weekly-off / work-schedule overrides
    public string? DefaultShiftName { get; set; }
    public TimeOnly? WorkStart { get; set; }
    public TimeOnly? WorkEnd { get; set; }
    public int? BreakMinutes { get; set; }
    public string? WeeklyOffPattern { get; set; }
    public string? WeeklyOffConfigJson { get; set; }
    public string? HolidayCalendarCode { get; set; }
    public int? GraceMinutes { get; set; }
    public bool? OvertimeAllowed { get; set; }
    public bool? WfhAllowed { get; set; }
    public string? AttendancePolicyCode { get; set; }

    // Leave & approval hierarchy (employee references)
    public Guid? LeaveApproverEmployeeId { get; set; }
    public Guid? AltLeaveApproverEmployeeId { get; set; }
    public Guid? OdApproverEmployeeId { get; set; }
    public Guid? CompOffApproverEmployeeId { get; set; }
    public Guid? OtApproverEmployeeId { get; set; }
    public Guid? RegularizationApproverEmployeeId { get; set; }
    public int? EscalationLevel { get; set; }

    // Staffing & budget
    public int? ApprovedHeadcount { get; set; }
    public decimal? BudgetAmount { get; set; }
    public string? WorkArea { get; set; }

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
