using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

public static class EmployeeDocumentStatuses
{
    public const string Uploaded = "uploaded";
    public const string Verified = "verified";
    public const string Rejected = "rejected";
    public const string Replaced = "replaced";
    public const string Expired = "expired";
}

/// <summary>Tenant-configured document category (identity, address proof, education, …).</summary>
[Audited("core.employee")]
public sealed class DocumentCategory : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Checklist item for onboarding: a missing required doc can block activation.</summary>
    public bool RequiredForOnboarding { get; set; }
    public int? RetentionYears { get; set; }
    /// <summary>Confidential docs audit every download.</summary>
    public bool Confidential { get; set; }
    /// <summary>Docs with expiry dates get swept to 'expired' by the daily job.</summary>
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>An uploaded employee document (file lives in files.file_objects; this row carries
/// category, verification, expiry and the replacement chain).</summary>
[Audited("core.employee")]
public sealed class EmployeeDocument : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid FileId { get; set; }
    public string? Label { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string Status { get; set; } = EmployeeDocumentStatuses.Uploaded;
    public Guid? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    /// <summary>Points at the superseding row when this document was replaced.</summary>
    public Guid? ReplacedById { get; set; }
    public int Version { get; set; } = 1;
    public uint RowVersion { get; set; }
}

/// <summary>A kind of letter that can be issued to employees (offer, appointment, experience, relieving, …).</summary>
[Audited("core.employee")]
public sealed class LetterType : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Employee whose name/designation signs the letter.</summary>
    public Guid? SignatoryEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Versioned Scriban template bound to a letter type. Old versions are kept because issued
/// letters snapshot the version they were rendered from.</summary>
[Audited("core.employee")]
public sealed class LetterTemplate : AuditableEntity, IConcurrencyAware
{
    public Guid LetterTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

public static class LetterStatuses
{
    public const string Issued = "issued";
    public const string Revoked = "revoked";
}

/// <summary>An issued letter — immutable rendered snapshot + the template version it came from.
/// It is never edited: revocation flips Status only.</summary>
[Audited("core.employee")]
public sealed class EmployeeLetter : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    public Guid LetterTypeId { get; set; }
    public Guid? LetterTemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string RenderedText { get; set; } = string.Empty;
    public string Status { get; set; } = LetterStatuses.Issued;
    public Guid IssuedBy { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
    public uint RowVersion { get; set; }
}
