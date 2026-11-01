using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

/// <summary>Confidential personal section — 1:1 with employees.</summary>
[Audited("core.employee")]
public sealed class EmployeePersonal : AuditableEntity, IConcurrencyAware
{
    /// <summary>PK = employee_id (1:1).</summary>
    public Guid EmployeeId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Nationality { get; set; }
    public string? BloodGroup { get; set; }
    public string? PersonalEmail { get; set; }
    public string? PersonalPhone { get; set; }
    /// <summary>jsonb: line1/line2/city/state/pin/country.</summary>
    public string? CurrentAddressJson { get; set; }
    public string? PermanentAddressJson { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Contact kinds for employee_contacts.</summary>
public static class ContactKinds
{
    public const string Emergency = "emergency";
    public const string Family = "family";
    public const string Nominee = "nominee";

    public static readonly string[] All = [Emergency, Family, Nominee];
}

/// <summary>Emergency/family/nominee contacts. Nominee share Σ ≤ 100 enforced in service.</summary>
[Audited("core.employee")]
public sealed class EmployeeContact : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    /// <summary>One of <see cref="ContactKinds"/>.</summary>
    public string Kind { get; set; } = ContactKinds.Emergency;
    public string Name { get; set; } = string.Empty;
    public string? Relation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    /// <summary>Nominee only: 1-100; Σ over nominee rows ≤ 100.</summary>
    public int? NomineeSharePct { get; set; }
    public uint RowVersion { get; set; }
}

public enum BankVerificationStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2,
}

/// <summary>Effective-dated bank accounts; account number encrypted via ISecretProtector.</summary>
[Audited("core.employee")]
public sealed class EmployeeBankAccount : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    [AuditMasked]
    public string AccountNumberEncrypted { get; set; } = string.Empty;
    public string AccountLast4 { get; set; } = string.Empty;
    public string Ifsc { get; set; } = string.Empty;
    public string? HolderName { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>NULL = active row; set when replaced.</summary>
    public DateOnly? EffectiveTo { get; set; }
    public BankVerificationStatus VerificationStatus { get; set; } = BankVerificationStatus.Pending;
    public Guid? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Statutory/tax identity section — 1:1; PAN/passport encrypted, DTOs expose last-4 only.</summary>
[Audited("core.employee")]
public sealed class EmployeeStatutory : AuditableEntity, IConcurrencyAware
{
    /// <summary>PK = employee_id (1:1).</summary>
    public Guid EmployeeId { get; set; }
    [AuditMasked]
    public string? PanEncrypted { get; set; }
    public string? PanLast4 { get; set; }
    public string? Uan { get; set; }
    public string? PfNumber { get; set; }
    public bool PfApplicable { get; set; } = true;
    public string? EsiNumber { get; set; }
    public bool EsiApplicable { get; set; }
    public string? PtState { get; set; }
    public bool LwfApplicable { get; set; }
    /// <summary>Last-4 only — full Aadhaar is never stored.</summary>
    public string? AadhaarLast4 { get; set; }
    [AuditMasked]
    public string? PassportEncrypted { get; set; }
    public string? PassportLast4 { get; set; }
    /// <summary>old_regime | new_regime.</summary>
    public string? TaxRegime { get; set; }
    public uint RowVersion { get; set; }
}
