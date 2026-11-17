using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

public sealed record LetterSignatoryInput(string Name, string? Designation, Guid? EmployeeId, bool IsActive, uint? RowVersion = null);
public sealed record LetterSignatoryDto(Guid Id, string Name, string? Designation, Guid? EmployeeId, bool IsActive, uint RowVersion);
public sealed record LetterheadInput(string Name, Guid? CompanyId, Guid? BranchId, Guid? LogoFileId, string? HeaderHtml, string? FooterHtml, int MarginMm, bool IsActive, uint? RowVersion = null);
public sealed record LetterheadDto(Guid Id, string Name, Guid? CompanyId, Guid? BranchId, Guid? LogoFileId, string? HeaderHtml, string? FooterHtml, int MarginMm, bool IsActive, uint RowVersion);
public sealed record LetterTemplateFamilyInput(Guid LetterTypeId, string Name);
public sealed record LetterTemplateFamilyDto(Guid Id, Guid LetterTypeId, string Name, int VersionCount);
public sealed record LetterTemplateVersionInput(
    string Name, string Subject, string Body,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    Guid? CompanyId, Guid? BranchId, Guid? LetterheadId, uint? RowVersion = null);
public sealed record LetterTemplateVersionDto(
    Guid Id, Guid FamilyId, string Name, int Version, string Status,
    string Subject, string Body, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    Guid? CompanyId, Guid? BranchId, Guid? LetterheadId, Guid? SupersedesId,
    DateTimeOffset? PublishedAt, DateTimeOffset? RetiredAt, uint RowVersion);

/// <summary>
/// Letter template administration: families, immutable published versions, letterheads and signatories.
/// Permission enforcement lives on the endpoints (template.manage / template.publish / letterhead.manage /
/// signatory.manage); this service owns data validation and the draft→published→retired lifecycle.
/// </summary>
public sealed class LetterAdminService(ITenantDbContext db, IClock clock, ITemplateRenderer renderer)
{
    public IReadOnlyList<LetterMergeFieldDto> MergeFields() => LetterMergeFields.Catalogue;

    // ---------- Signatories ----------

    public async Task<IReadOnlyList<LetterSignatoryDto>> ListSignatoriesAsync(CancellationToken ct) =>
        await db.LetterSignatories.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new LetterSignatoryDto(s.Id, s.Name, s.Designation, s.EmployeeId, s.IsActive, s.RowVersion))
            .ToListAsync(ct);

    public async Task<Result<Guid>> CreateSignatoryAsync(LetterSignatoryInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("signatory.name_required", "Signatory name is required.");
        }

        if (input.EmployeeId is { } empId && !await db.Employees.AnyAsync(e => e.Id == empId, ct))
        {
            return Error.Validation("signatory.employee_missing", "Signatory employee does not exist.");
        }

        var s = new LetterSignatory { Name = input.Name.Trim(), Designation = input.Designation?.Trim() ?? string.Empty, EmployeeId = input.EmployeeId, IsActive = input.IsActive };
        db.LetterSignatories.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task<Result> UpdateSignatoryAsync(Guid id, LetterSignatoryInput input, CancellationToken ct)
    {
        var s = await db.LetterSignatories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
        {
            return Error.NotFound("signatory.not_found", "Signatory not found.");
        }

        if (input.EmployeeId is { } empId && !await db.Employees.AnyAsync(e => e.Id == empId, ct))
        {
            return Error.Validation("signatory.employee_missing", "Signatory employee does not exist.");
        }

        s.Name = input.Name.Trim();
        s.Designation = input.Designation?.Trim() ?? string.Empty;
        s.EmployeeId = input.EmployeeId;
        s.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Letterheads ----------

    public async Task<IReadOnlyList<LetterheadDto>> ListLetterheadsAsync(CancellationToken ct) =>
        await db.Letterheads.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new LetterheadDto(l.Id, l.Name, l.CompanyId, l.BranchId, l.LogoFileId, l.HeaderHtml, l.FooterHtml, l.MarginMm, l.IsActive, l.RowVersion))
            .ToListAsync(ct);

    public async Task<Result<Guid>> CreateLetterheadAsync(LetterheadInput input, CancellationToken ct)
    {
        if (await ValidateLetterheadAsync(input, ct) is { } error)
        {
            return error;
        }

        var l = new Letterhead
        {
            Name = input.Name.Trim(), CompanyId = input.CompanyId, BranchId = input.BranchId,
            LogoFileId = input.LogoFileId, HeaderHtml = input.HeaderHtml, FooterHtml = input.FooterHtml,
            MarginMm = input.MarginMm, IsActive = input.IsActive,
        };
        db.Letterheads.Add(l);
        await db.SaveChangesAsync(ct);
        return l.Id;
    }

    public async Task<Result> UpdateLetterheadAsync(Guid id, LetterheadInput input, CancellationToken ct)
    {
        var l = await db.Letterheads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null)
        {
            return Error.NotFound("letterhead.not_found", "Letterhead not found.");
        }

        if (await ValidateLetterheadAsync(input, ct) is { } error)
        {
            return error;
        }

        l.Name = input.Name.Trim();
        l.CompanyId = input.CompanyId;
        l.BranchId = input.BranchId;
        l.LogoFileId = input.LogoFileId;
        l.HeaderHtml = input.HeaderHtml;
        l.FooterHtml = input.FooterHtml;
        l.MarginMm = input.MarginMm;
        l.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Error?> ValidateLetterheadAsync(LetterheadInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("letterhead.name_required", "Letterhead name is required.");
        }

        if (input.MarginMm is < 0 or > 40)
        {
            return Error.Validation("letterhead.margin_range", "Margin must be between 0 and 40 mm.");
        }

        if (input.BranchId is { } branchId)
        {
            var branchCompany = await db.Branches.Where(b => b.Id == branchId).Select(b => b.CompanyId).FirstOrDefaultAsync(ct);
            if (branchCompany == Guid.Empty)
            {
                return Error.Validation("letterhead.branch_missing", "Branch does not exist.");
            }

            if (input.CompanyId is { } companyId && companyId != branchCompany)
            {
                return Error.Validation("letterhead.branch_company_mismatch", "Branch does not belong to the selected company.");
            }
        }
        else if (input.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.Validation("letterhead.company_missing", "Company does not exist.");
        }

        if (input.LogoFileId is { } logoId && !await db.Files.AnyAsync(f => f.Id == logoId, ct))
        {
            return Error.Validation("letterhead.logo_missing", "Logo file does not exist.");
        }

        if (LetterHtml.Validate(input.HeaderHtml) is { Count: > 0 } headerProblems)
        {
            return Error.Validation("letterhead.header_html", "Header HTML: " + string.Join("; ", headerProblems));
        }

        if (LetterHtml.Validate(input.FooterHtml) is { Count: > 0 } footerProblems)
        {
            return Error.Validation("letterhead.footer_html", "Footer HTML: " + string.Join("; ", footerProblems));
        }

        return null;
    }

    // ---------- Template families + versions ----------

    public async Task<IReadOnlyList<LetterTemplateFamilyDto>> ListFamiliesAsync(Guid? letterTypeId, CancellationToken ct)
    {
        var q = db.LetterTemplateFamilies.AsNoTracking().AsQueryable();
        if (letterTypeId is { } lt)
        {
            q = q.Where(f => f.LetterTypeId == lt);
        }

        return await q.OrderBy(f => f.Name)
            .Select(f => new LetterTemplateFamilyDto(f.Id, f.LetterTypeId, f.Name, db.LetterTemplates.Count(t => t.FamilyId == f.Id)))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> CreateFamilyAsync(LetterTemplateFamilyInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("family.name_required", "Template family name is required.");
        }

        if (!await db.LetterTypes.AnyAsync(t => t.Id == input.LetterTypeId, ct))
        {
            return Error.Validation("letter_type.missing", "Letter type does not exist.");
        }

        var f = new LetterTemplateFamily { LetterTypeId = input.LetterTypeId, Name = input.Name.Trim() };
        db.LetterTemplateFamilies.Add(f);
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    public async Task<IReadOnlyList<LetterTemplateVersionDto>> ListVersionsAsync(Guid familyId, CancellationToken ct) =>
        await db.LetterTemplates.AsNoTracking().Where(t => t.FamilyId == familyId).OrderByDescending(t => t.Version)
            .Select(t => ToVersionDto(t)).ToListAsync(ct);

    public async Task<Result<LetterTemplateVersionDto>> GetVersionAsync(Guid templateId, CancellationToken ct)
    {
        var t = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId, ct);
        return t is null
            ? Error.NotFound("template.not_found", "Template not found.")
            : ToVersionDto(t);
    }

    /// <summary>Create a draft version in a family. When baseTemplateId is a published version,
    /// the draft clones it and records the supersedes link.</summary>
    public async Task<Result<Guid>> CreateDraftAsync(Guid familyId, Guid? baseTemplateId, LetterTemplateVersionInput input, CancellationToken ct)
    {
        var family = await db.LetterTemplateFamilies.AsNoTracking().FirstOrDefaultAsync(f => f.Id == familyId, ct);
        if (family is null)
        {
            return Error.NotFound("family.not_found", "Template family not found.");
        }

        LetterTemplate? source = null;
        if (baseTemplateId is { } baseId)
        {
            source = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == baseId && t.FamilyId == familyId, ct);
            if (source is null)
            {
                return Error.NotFound("template.not_found", "Base template not found in this family.");
            }
        }

        if (await ValidateTemplateInputAsync(input, ct) is { } error)
        {
            return error;
        }

        var version = (await db.LetterTemplates.Where(t => t.FamilyId == familyId).MaxAsync(t => (int?)t.Version, ct) ?? 0) + 1;
        var t = new LetterTemplate
        {
            LetterTypeId = family.LetterTypeId,
            FamilyId = familyId,
            Name = input.Name.Trim(),
            Version = version,
            Status = LetterTemplateStatuses.Draft,
            Subject = input.Subject,
            Body = input.Body,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            CompanyId = input.CompanyId,
            BranchId = input.BranchId,
            LetterheadId = input.LetterheadId,
            SupersedesId = source?.Id,
            IsActive = true,
        };
        db.LetterTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    /// <summary>Edit a draft in place. Published versions are immutable — edit via CreateDraftAsync(baseTemplateId).</summary>
    public async Task<Result> UpdateDraftAsync(Guid templateId, LetterTemplateVersionInput input, CancellationToken ct)
    {
        var t = await db.LetterTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (t is null)
        {
            return Error.NotFound("template.not_found", "Template not found.");
        }

        if (t.Status != LetterTemplateStatuses.Draft)
        {
            return Error.Conflict("template.immutable", "Published and retired versions are immutable; create a new draft from it instead.");
        }

        if (await ValidateTemplateInputAsync(input, ct) is { } error)
        {
            return error;
        }

        t.Name = input.Name.Trim();
        t.Subject = input.Subject;
        t.Body = input.Body;
        t.EffectiveFrom = input.EffectiveFrom;
        t.EffectiveTo = input.EffectiveTo;
        t.CompanyId = input.CompanyId;
        t.BranchId = input.BranchId;
        t.LetterheadId = input.LetterheadId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Draft → published. Retires the superseded version first so the exclusion constraint holds.</summary>
    public async Task<Result> PublishAsync(Guid templateId, Guid userId, CancellationToken ct)
    {
        var t = await db.LetterTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (t is null)
        {
            return Error.NotFound("template.not_found", "Template not found.");
        }

        if (t.Status == LetterTemplateStatuses.Published)
        {
            return Result.Success(); // idempotent
        }

        if (t.Status != LetterTemplateStatuses.Draft)
        {
            return Error.Conflict("template.immutable", "Only drafts can be published.");
        }

        // Pre-check the exclusion constraint so a same-scope overlap surfaces as 409, not a 500.
        var overlap = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id != t.Id && x.Id != t.SupersedesId &&
            x.LetterTypeId == t.LetterTypeId &&
            x.Status == LetterTemplateStatuses.Published &&
            x.CompanyId == t.CompanyId && x.BranchId == t.BranchId &&
            (x.EffectiveTo == null || x.EffectiveTo > t.EffectiveFrom) &&
            (t.EffectiveTo == null || t.EffectiveTo > x.EffectiveFrom), ct);
        if (overlap is not null)
        {
            return Error.Conflict("template.overlap",
                "A published version already covers an overlapping effective window at this applicability level; retire it or create a superseding draft.");
        }

        if (t.SupersedesId is { } supersedesId)
        {
            var old = await db.LetterTemplates.FirstOrDefaultAsync(x => x.Id == supersedesId, ct);
            if (old?.Status == LetterTemplateStatuses.Published)
            {
                old.Status = LetterTemplateStatuses.Retired;
                old.RetiredAt = clock.UtcNow;
                old.RetiredBy = userId;
            }
        }

        t.Status = LetterTemplateStatuses.Published;
        t.PublishedAt = clock.UtcNow;
        t.PublishedBy = userId;
        t.IsActive = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ex_letter_templates_no_overlap") == true)
        {
            // The race the pre-check cannot close: a concurrent publish landed between the
            // check and the save — translate the exclusion violation into the same 409.
            return Error.Conflict("template.overlap",
                "A published version already covers an overlapping effective window at this applicability level; retire it or create a superseding draft.");
        }
        return Result.Success();
    }

    /// <summary>Published (or draft) → retired. Retired versions are never used for issuance and stay immutable.</summary>
    public async Task<Result> RetireAsync(Guid templateId, Guid userId, CancellationToken ct)
    {
        var t = await db.LetterTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (t is null)
        {
            return Error.NotFound("template.not_found", "Template not found.");
        }

        if (t.Status == LetterTemplateStatuses.Retired)
        {
            return Result.Success();
        }

        t.Status = LetterTemplateStatuses.Retired;
        t.RetiredAt = clock.UtcNow;
        t.RetiredBy = userId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Error?> ValidateTemplateInputAsync(LetterTemplateVersionInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("template.name_required", "Template name is required.");
        }

        if (input.EffectiveTo is { } to && to <= input.EffectiveFrom)
        {
            return Error.Validation("template.date_range", "Effective-to must be after effective-from.");
        }

        if (input.BranchId is { } branchId)
        {
            var branchCompany = await db.Branches.Where(b => b.Id == branchId).Select(b => b.CompanyId).FirstOrDefaultAsync(ct);
            if (branchCompany == Guid.Empty)
            {
                return Error.Validation("template.branch_missing", "Branch does not exist.");
            }

            if (input.CompanyId is { } companyId && companyId != branchCompany)
            {
                return Error.Validation("template.branch_company_mismatch", "Branch does not belong to the selected company.");
            }
        }
        else if (input.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.Validation("template.company_missing", "Company does not exist.");
        }

        if (input.LetterheadId is { } lh && !await db.Letterheads.AnyAsync(l => l.Id == lh, ct))
        {
            return Error.Validation("template.letterhead_missing", "Letterhead does not exist.");
        }

        var htmlProblems = LetterHtml.Validate(input.Body).Concat(LetterHtml.Validate(input.Subject)).ToList();
        if (htmlProblems.Count > 0)
        {
            return Error.Validation("template.html", "Template HTML is not allowed: " + string.Join("; ", htmlProblems));
        }

        // Scriban syntax + size are validated at save so a broken or oversized template cannot
        // be published and fail later at render/PDF time.
        foreach (var text in new[] { input.Subject, input.Body })
        {
            if (System.Text.Encoding.UTF8.GetByteCount(text ?? string.Empty) > 256 * 1024)
            {
                return Error.Validation("template.too_large", "Template content exceeds the 256 KB limit.");
            }

            try
            {
                _ = renderer.Render(text ?? string.Empty, new Dictionary<string, object?>());
            }
            catch (Exception ex)
            {
                return Error.Validation("template.syntax", $"Template is not valid Scriban: {ex.Message}");
            }
        }

        var unknown = LetterMergeFields.ReferencedFields(input.Subject).Concat(LetterMergeFields.ReferencedFields(input.Body))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => CatalogueNames().All(c => !c.Equals(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation("template.fields", "Unknown or unsupported merge fields: " + string.Join(", ", unknown));
        }

        return null;
    }

    private static IEnumerable<string> CatalogueNames() => LetterMergeFields.Catalogue.Select(f => f.Name);

    private static LetterTemplateVersionDto ToVersionDto(LetterTemplate t) => new(
        t.Id, t.FamilyId, t.Name, t.Version, t.Status, t.Subject, t.Body,
        t.EffectiveFrom, t.EffectiveTo, t.CompanyId, t.BranchId, t.LetterheadId, t.SupersedesId,
        t.PublishedAt, t.RetiredAt, t.RowVersion);
}
