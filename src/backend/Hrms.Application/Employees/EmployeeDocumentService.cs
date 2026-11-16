using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Files;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

public sealed record DocumentCategoryInput(string Code, string Name, string? Description, bool RequiredForOnboarding, int? RetentionYears, bool Confidential, bool TrackExpiry, bool IsActive, uint? RowVersion = null);
public sealed record DocumentCategoryDto(Guid Id, string Code, string Name, string? Description, bool RequiredForOnboarding, int? RetentionYears, bool Confidential, bool TrackExpiry, bool IsActive, uint RowVersion);
public sealed record EmployeeDocumentDto(Guid Id, Guid EmployeeId, Guid CategoryId, string CategoryCode, string CategoryName,
    Guid FileId, string FileName, string? Label, string? DocumentNumber, DateOnly? IssuedOn, DateOnly? ExpiresOn,
    string Status, Guid? VerifiedBy, DateTimeOffset? VerifiedAt, string? VerificationNote, Guid? ReplacedById, int Version, uint RowVersion);
public sealed record DocumentVerifyInput(string Status, string? Note);
public sealed record LetterTypeInput(string Code, string Name, string? Description, Guid? SignatoryEmployeeId, bool IsActive, uint? RowVersion = null,
    // V2 fields — set-when-provided on update (null leaves the stored value, so the legacy UI can't clear them).
    Guid? SignatoryId = null, string? NumberPrefix = null, string? NumberReset = null);
public sealed record LetterTypeDto(Guid Id, string Code, string Name, string? Description, Guid? SignatoryEmployeeId, bool IsActive, uint RowVersion,
    Guid? SignatoryId, string NumberPrefix, string NumberReset);
public sealed record LetterTemplateInput(Guid LetterTypeId, string Name, string Subject, string Body, bool IsActive, uint? RowVersion = null);
public sealed record LetterTemplateDto(Guid Id, Guid LetterTypeId, string Name, int Version, string Subject, string Body, bool IsActive, uint RowVersion, string Status);
public sealed record IssueLetterInput(Guid LetterTypeId, Guid? LetterTemplateId);
public sealed record EmployeeLetterDto(Guid Id, Guid EmployeeId, Guid LetterTypeId, string LetterType, string Subject,
    string RenderedText, int TemplateVersion, string Status, Guid IssuedBy, DateTimeOffset IssuedAt,
    Guid? RevokedBy, DateTimeOffset? RevokedAt, string? RevokeReason, uint RowVersion,
    string LetterNumber, Guid? FileId, string? PdfSha256, string? SignatoryName, string? SignatoryDesignation,
    string? LetterheadSnapshot, DateOnly? IssueDate, string? IssueTz, Guid? SupersedesId);

/// <summary>Employee documents (categories, uploads via FileService, verification, expiry sweep, replacement
/// history) and letters (types, versioned Scriban templates, immutable issued snapshots, revocation).</summary>
public sealed class EmployeeDocumentService(
    ITenantDbContext db, FileService files, IDataScopeService scopes, IClock clock)
{
    /// <summary>Data-scope gate for employee-bound operations. Out-of-scope and non-existent employees
    /// are indistinguishable (404): the response must not be an existence oracle.</summary>
    private async Task<Error?> RequireEmployeeScopeAsync(string permissionCode, Guid employeeId, CancellationToken ct) =>
        await scopes.CanAccessEmployeeAsync(permissionCode, employeeId, ct)
            ? null
            : Error.NotFound("employee.not_found", "Employee not found.");

    // ---------- Categories ----------

    public async Task<IReadOnlyList<DocumentCategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await db.DocumentCategories.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new DocumentCategoryDto(c.Id, c.Code, c.Name, c.Description, c.RequiredForOnboarding, c.RetentionYears, c.Confidential, c.TrackExpiry, c.IsActive, c.RowVersion))
            .ToListAsync(ct);

    public async Task<Result<Guid>> UpsertCategoryAsync(DocumentCategoryInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToLowerInvariant();
        if (code.Length is 0 or > 60 || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("category.invalid", "A category code (1-60 chars) and name are required.");
        }

        var existing = await db.DocumentCategories.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (existing is null)
        {
            var c = new DocumentCategory
            {
                Code = code, Name = input.Name.Trim(), Description = input.Description?.Trim(),
                RequiredForOnboarding = input.RequiredForOnboarding, RetentionYears = input.RetentionYears,
                Confidential = input.Confidential, TrackExpiry = input.TrackExpiry, IsActive = input.IsActive,
            };
            db.DocumentCategories.Add(c);
            await db.SaveChangesAsync(ct);
            return c.Id;
        }

        if (input.RowVersion is { } rv && rv != existing.RowVersion)
        {
            return Error.Conflict("stale-version", "This record was modified elsewhere. Reload and retry.");
        }

        existing.Name = input.Name.Trim();
        existing.Description = input.Description?.Trim();
        existing.RequiredForOnboarding = input.RequiredForOnboarding;
        existing.RetentionYears = input.RetentionYears;
        existing.Confidential = input.Confidential;
        existing.TrackExpiry = input.TrackExpiry;
        existing.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    // ---------- Documents ----------

    public async Task<Result<IReadOnlyList<EmployeeDocumentDto>>> ListDocumentsAsync(Guid employeeId, CancellationToken ct)
    {
        if (await RequireEmployeeScopeAsync(FoundationPermissions.DocumentsView, employeeId, ct) is { } scopeError)
        {
            return scopeError;
        }

        var rows = await (
            from d in db.EmployeeDocuments.AsNoTracking()
            join c in db.DocumentCategories.AsNoTracking() on d.CategoryId equals c.Id
            join f in db.Files.AsNoTracking() on d.FileId equals f.Id
            where d.EmployeeId == employeeId
            orderby d.CreatedAt descending
            select new EmployeeDocumentDto(d.Id, d.EmployeeId, d.CategoryId, c.Code, c.Name, d.FileId, f.OriginalFileName,
                d.Label, d.DocumentNumber, d.IssuedOn, d.ExpiresOn, d.Status, d.VerifiedBy, d.VerifiedAt,
                d.VerificationNote, d.ReplacedById, d.Version, d.RowVersion))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<Result<EmployeeDocumentDto>> UploadAsync(Guid employeeId, Guid categoryId, FileUploadRequest file,
        string? label, string? documentNumber, DateOnly? issuedOn, DateOnly? expiresOn, Guid? replacesDocumentId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        if (await RequireEmployeeScopeAsync(FoundationPermissions.DocumentsUpload, employeeId, ct) is { } scopeError)
        {
            return scopeError;
        }

        var category = await db.DocumentCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null || !category.IsActive)
        {
            return Error.Validation("category.inactive", "Unknown or inactive document category.");
        }

        if (expiresOn is { } exp && !category.TrackExpiry)
        {
            return Error.Validation("expiry.not_tracked", $"Category '{category.Code}' does not track expiry dates.");
        }

        EmployeeDocument? replaced = null;
        if (replacesDocumentId is { } rid)
        {
            replaced = await db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == rid && d.EmployeeId == employeeId, ct);
            if (replaced is null || replaced.Status == EmployeeDocumentStatuses.Replaced)
            {
                return Error.NotFound("document.not_found", "Document to replace not found.");
            }
        }

        var uploaded = await files.UploadAsync(file with { OwnerEntityType = "employee", OwnerEntityId = employeeId.ToString() }, ct);
        if (!uploaded.IsSuccess)
        {
            return uploaded.Error!;
        }

        var doc = new EmployeeDocument
        {
            EmployeeId = employeeId,
            CategoryId = categoryId,
            FileId = uploaded.Value.Id,
            Label = label?.Trim(),
            DocumentNumber = documentNumber?.Trim(),
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
            Status = expiresOn is { } ex && ex < Today() ? EmployeeDocumentStatuses.Expired : EmployeeDocumentStatuses.Uploaded,
            Version = (replaced?.Version ?? 0) + 1,
        };
        db.EmployeeDocuments.Add(doc);

        if (replaced is not null)
        {
            replaced.Status = EmployeeDocumentStatuses.Replaced;
            replaced.ReplacedById = doc.Id;
        }

        await db.SaveChangesAsync(ct);
        return new EmployeeDocumentDto(doc.Id, doc.EmployeeId, doc.CategoryId, category.Code, category.Name, doc.FileId,
            file.FileName, doc.Label, doc.DocumentNumber, doc.IssuedOn, doc.ExpiresOn, doc.Status,
            doc.VerifiedBy, doc.VerifiedAt, doc.VerificationNote, doc.ReplacedById, doc.Version, doc.RowVersion);
    }

    public async Task<Result> VerifyAsync(Guid employeeId, Guid documentId, DocumentVerifyInput input, Guid verifierId, CancellationToken ct)
    {
        var doc = await db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, ct);
        if (doc is null)
        {
            return Error.NotFound("document.not_found", "Document not found.");
        }

        if (await RequireEmployeeScopeAsync(FoundationPermissions.DocumentsVerify, employeeId, ct) is { } scopeError)
        {
            return scopeError;
        }

        if (doc.Status is EmployeeDocumentStatuses.Replaced)
        {
            return Error.Conflict("document.replaced", "A replaced document cannot be verified.");
        }

        var status = input.Status.Trim().ToLowerInvariant();
        if (status is not (EmployeeDocumentStatuses.Verified or EmployeeDocumentStatuses.Rejected))
        {
            return Error.Validation("verify.invalid", "Status must be 'verified' or 'rejected'.");
        }

        doc.Status = status;
        doc.VerifiedBy = verifierId;
        doc.VerifiedAt = clock.UtcNow;
        doc.VerificationNote = input.Note;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Daily sweep leg: marks expired documents (expiry date passed, still live status).</summary>
    public async Task<int> ExpireDueDocumentsAsync(DateOnly today, CancellationToken ct)
    {
        var due = await db.EmployeeDocuments
            .Where(d => d.ExpiresOn != null && d.ExpiresOn < today &&
                        d.Status != EmployeeDocumentStatuses.Expired && d.Status != EmployeeDocumentStatuses.Replaced)
            .ToListAsync(ct);
        foreach (var d in due)
        {
            d.Status = EmployeeDocumentStatuses.Expired;
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return due.Count;
    }

    /// <summary>Onboarding checklist: required active categories missing a non-rejected doc for the employee.</summary>
    public async Task<Result<IReadOnlyList<DocumentCategoryDto>>> MissingOnboardingDocsAsync(Guid employeeId, CancellationToken ct)
    {
        if (await RequireEmployeeScopeAsync(FoundationPermissions.DocumentsView, employeeId, ct) is { } scopeError)
        {
            return scopeError;
        }

        var have = await db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.Status != EmployeeDocumentStatuses.Rejected && d.Status != EmployeeDocumentStatuses.Replaced)
            .Select(d => d.CategoryId).ToListAsync(ct);
        return await db.DocumentCategories.AsNoTracking()
            .Where(c => c.RequiredForOnboarding && c.IsActive && !have.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new DocumentCategoryDto(c.Id, c.Code, c.Name, c.Description, c.RequiredForOnboarding, c.RetentionYears, c.Confidential, c.TrackExpiry, c.IsActive, c.RowVersion))
            .ToListAsync(ct);
    }

    // ---------- Letter types & templates ----------

    public async Task<IReadOnlyList<LetterTypeDto>> ListLetterTypesAsync(CancellationToken ct) =>
        await db.LetterTypes.AsNoTracking().OrderBy(t => t.Code)
            .Select(t => new LetterTypeDto(t.Id, t.Code, t.Name, t.Description, t.SignatoryEmployeeId, t.IsActive, t.RowVersion,
                t.SignatoryId, t.NumberPrefix, t.NumberReset))
            .ToListAsync(ct);

    public async Task<Result<Guid>> UpsertLetterTypeAsync(LetterTypeInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToLowerInvariant();
        if (code.Length is 0 or > 60 || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("letter_type.invalid", "A type code (1-60 chars) and name are required.");
        }

        if (input.SignatoryEmployeeId is { } sid && !await db.Employees.AnyAsync(e => e.Id == sid, ct))
        {
            return Error.Validation("letter_type.signatory", "Signatory employee not found.");
        }

        if (input.SignatoryId is { } sigId && !await db.LetterSignatories.AnyAsync(s => s.Id == sigId, ct))
        {
            return Error.Validation("letter_type.signatory_missing", "Signatory not found.");
        }

        var prefix = input.NumberPrefix is null ? null : input.NumberPrefix.Trim().ToUpperInvariant();
        if (prefix is { Length: 0 or > 40 })
        {
            return Error.Validation("letter_type.number_prefix", "Number prefix must be 1-40 characters.");
        }

        if (input.NumberReset is { } reset && reset is not ("never" or "yearly" or "monthly"))
        {
            return Error.Validation("letter_type.number_reset", "Number reset must be 'never', 'yearly' or 'monthly'.");
        }

        var existing = await db.LetterTypes.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (existing is null)
        {
            var t = new LetterType { Code = code, Name = input.Name.Trim(), Description = input.Description?.Trim(), SignatoryEmployeeId = input.SignatoryEmployeeId, IsActive = input.IsActive };
            t.SignatoryId = input.SignatoryId;
            if (prefix is not null)
            {
                t.NumberPrefix = prefix;
            }

            if (input.NumberReset is not null)
            {
                t.NumberReset = input.NumberReset;
            }

            db.LetterTypes.Add(t);
            await db.SaveChangesAsync(ct);
            return t.Id;
        }

        if (input.RowVersion is { } rv && rv != existing.RowVersion)
        {
            return Error.Conflict("stale-version", "This record was modified elsewhere. Reload and retry.");
        }

        existing.Name = input.Name.Trim();
        existing.Description = input.Description?.Trim();
        existing.SignatoryEmployeeId = input.SignatoryEmployeeId;
        existing.IsActive = input.IsActive;
        if (input.SignatoryId is { } newSig)
        {
            existing.SignatoryId = newSig;
        }

        if (prefix is not null)
        {
            existing.NumberPrefix = prefix;
        }

        if (input.NumberReset is not null)
        {
            existing.NumberReset = input.NumberReset;
        }

        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    public async Task<IReadOnlyList<LetterTemplateDto>> ListTemplatesAsync(Guid letterTypeId, CancellationToken ct) =>
        await db.LetterTemplates.AsNoTracking().Where(t => t.LetterTypeId == letterTypeId).OrderByDescending(t => t.Version)
            .Select(t => new LetterTemplateDto(t.Id, t.LetterTypeId, t.Name, t.Version, t.Subject, t.Body, t.IsActive, t.RowVersion, t.Status))
            .ToListAsync(ct);

    public async Task<Result<Guid>> CreateTemplateAsync(LetterTemplateInput input, CancellationToken ct)
    {
        if (!await db.LetterTypes.AnyAsync(t => t.Id == input.LetterTypeId, ct))
        {
            return Error.NotFound("letter_type.not_found", "Letter type not found.");
        }

        if (string.IsNullOrWhiteSpace(input.Body))
        {
            return Error.Validation("template.body", "Template body is required.");
        }

        // M2.7 V2 compat: the legacy endpoint always produced an immediately-issuable template.
        // It now creates its own family and a published version (tenant-wide, effective today).
        var htmlProblems = LetterHtml.Validate(input.Body).Concat(LetterHtml.Validate(input.Subject)).ToList();
        if (htmlProblems.Count > 0)
        {
            return Error.Validation("template.html", "Template HTML is not allowed: " + string.Join("; ", htmlProblems));
        }

        var family = await db.LetterTemplateFamilies
            .FirstOrDefaultAsync(f => f.LetterTypeId == input.LetterTypeId && f.Name == input.Name.Trim(), ct);
        if (family is null)
        {
            family = new LetterTemplateFamily { LetterTypeId = input.LetterTypeId, Name = input.Name.Trim() };
            db.LetterTemplateFamilies.Add(family);
        }

        var previous = await db.LetterTemplates
            .Where(t => t.FamilyId == family.Id && t.Status == LetterTemplateStatuses.Published)
            .OrderByDescending(t => t.Version).FirstOrDefaultAsync(ct);
        var version = (await db.LetterTemplates.Where(t => t.FamilyId == family.Id).MaxAsync(t => (int?)t.Version, ct) ?? 0) + 1;
        var t = new LetterTemplate
        {
            LetterTypeId = input.LetterTypeId, FamilyId = family.Id, Name = input.Name.Trim(), Version = version,
            Subject = input.Subject.Trim(), Body = input.Body, IsActive = input.IsActive,
            Status = LetterTemplateStatuses.Published, EffectiveFrom = Today(),
            PublishedAt = clock.UtcNow, SupersedesId = previous?.Id,
        };
        if (previous is not null)
        {
            previous.Status = LetterTemplateStatuses.Retired;
            previous.RetiredAt = clock.UtcNow;
        }
        db.LetterTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    // ---------- Issued letters ----------
    // Issuance lives in LetterIssuanceService (M2.7 V2: numbering + two-transaction PDF workflow).

    public async Task<Result<IReadOnlyList<EmployeeLetterDto>>> ListLettersAsync(Guid employeeId, CancellationToken ct)
    {
        if (await RequireEmployeeScopeAsync(FoundationPermissions.LettersView, employeeId, ct) is { } scopeError)
        {
            return scopeError;
        }

        var rows = await (
            from l in db.EmployeeLetters.AsNoTracking()
            join t in db.LetterTypes.AsNoTracking() on l.LetterTypeId equals t.Id
            where l.EmployeeId == employeeId
            orderby l.IssuedAt descending
            select new { l, t })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.l, r.t)).ToList();
    }

    public async Task<Result<EmployeeLetterDto>> GetLetterAsync(Guid letterId, CancellationToken ct)
    {
        var row = await (
            from l in db.EmployeeLetters.AsNoTracking()
            join t in db.LetterTypes.AsNoTracking() on l.LetterTypeId equals t.Id
            where l.Id == letterId
            select new { l, t })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        // Resolve the owning employee before authorizing the id-based read.
        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersView, row.l.EmployeeId, ct))
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        return ToDto(row.l, row.t);
    }

    public async Task<Result> RevokeAsync(Guid letterId, Guid revokerId, string? reason, CancellationToken ct)
    {
        var letter = await db.EmployeeLetters.FirstOrDefaultAsync(l => l.Id == letterId, ct);
        if (letter is null)
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersRevoke, letter.EmployeeId, ct))
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        if (letter.Status == LetterStatuses.Revoked)
        {
            return Error.Conflict("letter.already_revoked", "This letter is already revoked.");
        }

        letter.Status = LetterStatuses.Revoked;
        letter.RevokedBy = revokerId;
        letter.RevokedAt = clock.UtcNow;
        letter.RevokeReason = reason;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.DateTime);

    private static EmployeeLetterDto ToDto(EmployeeLetter l, LetterType t) => new(
        l.Id, l.EmployeeId, l.LetterTypeId, t.Name, l.Subject, l.RenderedText, l.TemplateVersion,
        l.Status, l.IssuedBy, l.IssuedAt, l.RevokedBy, l.RevokedAt, l.RevokeReason, l.RowVersion,
        l.LetterNumber, l.FileId, l.PdfSha256, l.SignatoryName, l.SignatoryDesignation,
        l.LetterheadSnapshot, l.IssueDate, l.IssueTz, l.SupersedesId);
}
