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

/// <summary>Admin-managed signatory master for letters. Name/designation are snapshotted onto
/// issued letters so they stay correct even if the master later changes.</summary>
[Audited("core.employee")]
public sealed class LetterSignatory : AuditableEntity, IConcurrencyAware
{
    public string Name { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    /// <summary>Optional link to the employee the signatory represents (informational).</summary>
    public Guid? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Reusable letterhead: header/footer HTML + logo, optionally bound to a company or branch.</summary>
[Audited("core.employee")]
public sealed class Letterhead : AuditableEntity, IConcurrencyAware
{
    public string Name { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? LogoFileId { get; set; }
    public string? HeaderHtml { get; set; }
    public string? FooterHtml { get; set; }
    public int MarginMm { get; set; } = 15;
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Grouping identity for a template's version chain. Editing a published template
/// creates a new draft row inside the same family.</summary>
[Audited("core.employee")]
public sealed class LetterTemplateFamily : AuditableEntity, IConcurrencyAware
{
    public Guid LetterTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
}

/// <summary>Atomic counter row for letter number allocation. Upserted inside the issuance
/// transaction so allocation is atomic and every allocated number maps to a letter row.</summary>
public sealed class LetterCounter
{
    /// <summary>'letter.{prefix}:{period}' — period is the year or 'all'. The prefix namespaces
    /// the sequence so letter_number stays globally unique across types sharing a prefix.</summary>
    public string ScopeKey { get; set; } = string.Empty;
    public long LastNumber { get; set; }
    public string Prefix { get; set; } = "LTR";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A kind of letter that can be issued to employees (offer, appointment, experience, relieving, …).</summary>
[Audited("core.employee")]
public sealed class LetterType : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Employee whose name/designation signs the letter (legacy; dual-read fallback).</summary>
    public Guid? SignatoryEmployeeId { get; set; }
    /// <summary>Signatory master (preferred). Falls back to SignatoryEmployeeId.</summary>
    public Guid? SignatoryId { get; set; }
    /// <summary>Prefix for issued letter numbers, e.g. 'APPT'. Default 'LTR'.</summary>
    public string NumberPrefix { get; set; } = "LTR";
    /// <summary>Counter reset period: 'yearly' or 'never'.</summary>
    public string NumberReset { get; set; } = "yearly";
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

public static class LetterTemplateStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";
}

/// <summary>Versioned Scriban template bound to a letter type via a family. Published versions are
/// immutable; only published, in-window rows participate in issue/preview resolution.</summary>
[Audited("core.employee")]
public sealed class LetterTemplate : AuditableEntity, IConcurrencyAware
{
    public Guid LetterTypeId { get; set; }
    /// <summary>Version chain identity; unique (FamilyId, Version).</summary>
    public Guid FamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    /// <summary>'draft' | 'published' | 'retired'.</summary>
    public string Status { get; set; } = LetterTemplateStatuses.Draft;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>[from, to) date window evaluated in the tenant timezone; to exclusive, null = open-ended.
    /// from is always set (defaults to the draft's creation date).</summary>
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Applicability: branch+company, company-only, or tenant-wide (both null).</summary>
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? LetterheadId { get; set; }
    /// <summary>Points at the published version this draft was derived from.</summary>
    public Guid? SupersedesId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public Guid? RetiredBy { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

public static class LetterStatuses
{
    /// <summary>Number reserved; generation (render + PDF) not yet complete.</summary>
    public const string Issuing = "issuing";
    public const string Issued = "issued";
    /// <summary>Generation failed after number reservation; retry reuses the same number.</summary>
    public const string GenerationFailed = "generation_failed";
    public const string Revoked = "revoked";
    /// <summary>Replaced by a reissued letter; file and row are preserved.</summary>
    public const string Superseded = "superseded";
}

/// <summary>An issued letter — immutable rendered snapshot + the template version it came from.
/// It is never edited: revocation/supersession flips Status only, files are never deleted.</summary>
[Audited("core.employee")]
public sealed class EmployeeLetter : AuditableEntity, IConcurrencyAware
{
    public Guid EmployeeId { get; set; }
    public Guid LetterTypeId { get; set; }
    public Guid? LetterTemplateId { get; set; }
    public int TemplateVersion { get; set; }
    /// <summary>Allocated at reservation; unique, never reused, gaps allowed (audit provenance).
    /// 'LEGACY-<id>' on rows issued before M2.7.</summary>
    public string LetterNumber { get; set; } = string.Empty;
    /// <summary>Immutable issued PDF in files.files (owner = employee).</summary>
    public Guid? FileId { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>HTML snapshot of the issued letter (rendered output).</summary>
    public string? RenderedHtml { get; set; }
    public string RenderedText { get; set; } = string.Empty;
    public string? SignatoryName { get; set; }
    public string? SignatoryDesignation { get; set; }
    /// <summary>Resolved letterhead/branding at issue time (jsonb).</summary>
    public string? LetterheadSnapshot { get; set; }
    /// <summary>Issue date in the tenant timezone (used for numbering period).</summary>
    public DateOnly? IssueDate { get; set; }
    public string? IssueTz { get; set; }
    /// <summary>SHA-256 (hex) of the stored PDF bytes.</summary>
    public string? PdfSha256 { get; set; }
    /// <summary>Points at the letter this one replaces (reissue chain).</summary>
    public Guid? SupersedesId { get; set; }
    public string Status { get; set; } = LetterStatuses.Issued;
    public Guid IssuedBy { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
    public uint RowVersion { get; set; }
}
