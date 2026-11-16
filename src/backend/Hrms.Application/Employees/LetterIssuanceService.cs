using System.Security.Cryptography;
using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Files;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

public sealed record LetterPreviewDto(string Subject, string Html, Guid TemplateId, int TemplateVersion, string LetterNumber);
public sealed record IssueLetterResult(Guid LetterId, string LetterNumber, Guid? FileId, string Status);
public sealed record RetryLetterResult(Guid LetterId, string LetterNumber, Guid? FileId, string Status);

/// <summary>
/// Letter issuance: preview, atomic number reservation, render + PDF generation, immutable issuance.
///
/// Transaction contract (approved design):
///  - TX-1 reserves the letter number and inserts the `issuing` row — committed before any external call.
///  - Render + Gotenberg run OUTSIDE any database transaction.
///  - TX-2 stores the PDF as a file row and marks the letter `issued` with all snapshots + sha-256.
///  - Any failure flips the letter to `generation_failed`; the number stays consumed forever and the
///    retry endpoint reuses it. Every allocated number therefore maps to exactly one issuance record.
/// </summary>
public sealed class LetterIssuanceService(
    ITenantDbContext db, FileService files, IFileStorageService storage, ITemplateRenderer renderer,
    IPdfService pdf, IDataScopeService scopes, IClock clock)
{
    private const int MaxHtmlBytes = 256 * 1024;

    // ---------- Applicability resolution ----------

    /// <summary>branch > company > tenant-wide; among candidates the highest version wins.</summary>
    private static IQueryable<LetterTemplate> Applicable(Guid typeId, Employee e, DateOnly today, ITenantDbContext db) =>
        db.LetterTemplates.AsNoTracking().Where(t =>
            t.LetterTypeId == typeId &&
            t.Status == LetterTemplateStatuses.Published &&
            t.EffectiveFrom <= today &&
            (t.EffectiveTo == null || t.EffectiveTo > today) &&
            ((t.BranchId == e.BranchId) ||
             (t.BranchId == null && t.CompanyId == e.CompanyId) ||
             (t.BranchId == null && t.CompanyId == null)));

    private async Task<Result<LetterTemplate>> ResolveTemplateAsync(LetterType type, Employee e, Guid? templateId, DateOnly today, CancellationToken ct)
    {
        if (templateId is { } tid)
        {
            var t = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid && x.LetterTypeId == type.Id, ct);
            if (t is null)
            {
                return Error.NotFound("template.not_found", "Template not found for this letter type.");
            }

            return t.Status != LetterTemplateStatuses.Published
                ? Error.Conflict("template.not_published", "Only published template versions can be issued.")
                : t;
        }

        var candidates = await Applicable(type.Id, e, today, db)
            .OrderByDescending(t => t.BranchId != null ? 2 : t.CompanyId != null ? 1 : 0)
            .ThenByDescending(t => t.Version)
            .ToListAsync(ct);
        var selected = candidates.FirstOrDefault();
        return selected is null
            ? Error.Validation("template.missing", "No published template applies to this employee for this letter type.")
            : selected;
    }

    private async Task<DateOnly> TodayInBranchTzAsync(Employee e, CancellationToken ct)
    {
        var tzName = e.BranchId is { } bid
            ? await db.Branches.Where(b => b.Id == bid).Select(b => b.TimeZone).FirstOrDefaultAsync(ct)
            : null;
        try
        {
            var tz = tzName is { Length: > 0 } ? TimeZoneInfo.FindSystemTimeZoneById(tzName) : TimeZoneInfo.Utc;
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, tz).DateTime);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(clock.UtcNow.DateTime);
        }
    }

    private async Task<string> BranchTzNameAsync(Employee e, CancellationToken ct) =>
        e.BranchId is { } bid
            ? (await db.Branches.Where(b => b.Id == bid).Select(b => b.TimeZone).FirstOrDefaultAsync(ct)) ?? "Asia/Kolkata"
            : "Asia/Kolkata";

    // ---------- Numbering ----------

    /// <summary>Atomic counter upsert; runs inside the caller's TX-1 so number + issuing row commit together.</summary>
    private async Task<string> AllocateNumberAsync(LetterType type, DateOnly today, CancellationToken ct)
    {
        var period = type.NumberReset switch
        {
            "yearly" => today.Year.ToString("0000"),
            "monthly" => today.ToString("yyyy-MM"),
            _ => "all",
        };
        // The sequence namespace is the formatted prefix itself, not the letter type: a global
        // UNIQUE(letter_number) means two types sharing a prefix must share one counter, else both
        // allocate seq 1 and collide. Normalized to invariant uppercase so 'appt'/'APPT' share.
        var prefix = type.NumberPrefix?.Trim().ToUpperInvariant() is { Length: > 0 } p ? p : "LTR";
        var scopeKey = $"letter.{prefix}:{period}";

        var next = await db.Database
            .SqlQuery<long>($"""
                insert into hr.letter_counters (scope_key, last_number, prefix, updated_at)
                values ({scopeKey}, 1, {prefix}, now())
                on conflict (scope_key) do update
                  set last_number = hr.letter_counters.last_number + 1, updated_at = now()
                returning last_number
                """)
            .AsAsyncEnumerable()
            .SingleAsync(ct);

        return period == "all"
            ? $"{prefix}-{next:000000}"
            : $"{prefix}-{period}-{next:000000}";
    }

    // ---------- Letterhead snapshot ----------

    private async Task<(string snapshotJson, string? headerHtml, string? footerHtml, int marginMm)> SnapshotLetterheadAsync(LetterTemplate t, Employee e, CancellationToken ct)
    {
        var lh = t.LetterheadId is { } lid
            ? await db.Letterheads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lid, ct)
            : await db.Letterheads.AsNoTracking().Where(l => l.IsActive)
                .OrderByDescending(l => l.BranchId == e.BranchId ? 2 : l.CompanyId == e.CompanyId ? 1 : 0)
                .FirstOrDefaultAsync(l =>
                    (l.BranchId == e.BranchId) ||
                    (l.BranchId == null && l.CompanyId == e.CompanyId) ||
                    (l.BranchId == null && l.CompanyId == null), ct);

        if (lh is null)
        {
            return ("{}", null, null, 15);
        }

        var header = lh.HeaderHtml;
        if (lh.LogoFileId is { } logoId)
        {
            var logo = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == logoId, ct);
            if (logo is not null)
            {
                await using var stream = await storage.OpenReadAsync(logo.StorageKey, ct);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                var dataUri = $"data:{logo.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";
                header = $"<div class=\"lh-brand\"><img src=\"{dataUri}\" style=\"max-height:60px\"/></div>" + (header ?? string.Empty);
            }
        }

        var snapshot = JsonSerializer.Serialize(new
        {
            lh.Id, lh.Name, lh.HeaderHtml, lh.FooterHtml, lh.MarginMm, lh.LogoFileId,
        });
        return (snapshot, header, lh.FooterHtml, lh.MarginMm);
    }

    // ---------- Preview ----------

    public async Task<Result<LetterPreviewDto>> PreviewAsync(Guid employeeId, Guid letterTypeId, Guid? templateId, CancellationToken ct)
    {
        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersIssue, employeeId, ct))
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var type = await db.LetterTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == letterTypeId, ct);
        if (type is null || !type.IsActive)
        {
            return Error.Validation("letter_type.inactive", "Unknown or inactive letter type.");
        }

        var today = await TodayInBranchTzAsync(employee, ct);
        var templateResult = await ResolveTemplateAsync(type, employee, templateId, today, ct);
        if (!templateResult.IsSuccess)
        {
            return templateResult.Error!;
        }
        var template = templateResult.Value;

        var model = await LetterMergeFields.BuildAsync(db, clock, employee, type, null, today, ct);
        string subject, body;
        try
        {
            subject = renderer.Render(template.Subject, model);
            body = renderer.Render(template.Body, model);
        }
        catch (Exception ex)
        {
            return Error.Rule("template.render", $"Template failed to render: {ex.Message}");
        }
        var (_, headerHtml, footerHtml, marginMm) = await SnapshotLetterheadAsync(template, employee, ct);
        var html = LetterHtml.ComposeDocument(subject, headerHtml, body, footerHtml, marginMm);
        return new LetterPreviewDto(subject, html, template.Id, template.Version, string.Empty);
    }

    // ---------- Issue (two-transaction workflow) ----------

    public async Task<Result<IssueLetterResult>> IssueAsync(Guid employeeId, Guid letterTypeId, Guid? templateId, Guid issuerId, CancellationToken ct)
    {
        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersIssue, employeeId, ct))
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var type = await db.LetterTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == letterTypeId, ct);
        if (type is null || !type.IsActive)
        {
            return Error.Validation("letter_type.inactive", "Unknown or inactive letter type.");
        }

        var today = await TodayInBranchTzAsync(employee, ct);
        var tzName = await BranchTzNameAsync(employee, ct);
        var templateResult = await ResolveTemplateAsync(type, employee, templateId, today, ct);
        if (!templateResult.IsSuccess)
        {
            return templateResult.Error!;
        }
        return await IssueCoreAsync(employee, type, templateResult.Value, today, tzName, issuerId, null, ct);
    }

    /// <summary>Reissue: a fresh letter (new allocated number) superseding the original. The original
    /// keeps its number, file and history; its status flips issued→superseded atomically with the new
    /// issue. Resolved against the currently published template, so a retired original is replaced by
    /// the newer version.</summary>
    public async Task<Result<IssueLetterResult>> ReissueAsync(Guid letterId, Guid issuerId, CancellationToken ct)
    {
        var old = await db.EmployeeLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == letterId, ct);
        if (old is null)
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        // Resolve the owning employee before authorizing the id-based operation.
        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersReissue, old.EmployeeId, ct))
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        if (old.Status is not (LetterStatuses.Issued or LetterStatuses.Revoked))
        {
            return Error.Rule("letter.not_reissuable", "Only issued or revoked letters can be reissued.");
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == old.EmployeeId, ct);
        if (employee is null)
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        var type = await db.LetterTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == old.LetterTypeId, ct);
        if (type is null || !type.IsActive)
        {
            return Error.Validation("letter_type.inactive", "Unknown or inactive letter type.");
        }

        var today = await TodayInBranchTzAsync(employee, ct);
        var tzName = await BranchTzNameAsync(employee, ct);
        var templateResult = await ResolveTemplateAsync(type, employee, null, today, ct);
        if (!templateResult.IsSuccess)
        {
            return templateResult.Error!;
        }

        return await IssueCoreAsync(employee, type, templateResult.Value, today, tzName, issuerId, letterId, ct);
    }

    private async Task<Result<IssueLetterResult>> IssueCoreAsync(
        Employee employee, LetterType type, LetterTemplate template,
        DateOnly today, string tzName, Guid issuerId, Guid? supersedesId, CancellationToken ct)
    {
        // TX-1: number reservation + issuing record commit together — atomic, short-lived.
        // NpgsqlRetryingExecutionStrategy requires user-initiated transactions to run
        // inside CreateExecutionStrategy() so the whole unit is retriable.
        var letter = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var number = await AllocateNumberAsync(type, today, ct);
            var l = new EmployeeLetter
            {
                EmployeeId = employee.Id,
                LetterTypeId = type.Id,
                LetterTemplateId = template.Id,
                TemplateVersion = template.Version,
                LetterNumber = number,
                Status = LetterStatuses.Issuing,
                IssuedBy = issuerId,
                IssuedAt = clock.UtcNow,
                Subject = string.Empty,
                IssueTz = tzName,
                SupersedesId = supersedesId,
            };
            db.EmployeeLetters.Add(l);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return l;
        });

        return await GenerateAsync(letter, type, template, employee, today, ct);
    }

    /// <summary>Render → PDF → file row → mark issued. Retried via RetryAsync with the same number.</summary>
    private async Task<Result<IssueLetterResult>> GenerateAsync(EmployeeLetter letter, LetterType type, LetterTemplate template, Employee employee, DateOnly today, CancellationToken ct)
    {
        try
        {
            var model = await LetterMergeFields.BuildAsync(db, clock, employee, type, letter.LetterNumber, today, ct);
            var subject = renderer.Render(template.Subject, model);
            var body = renderer.Render(template.Body, model);
            var (snapshotJson, headerHtml, footerHtml, marginMm) = await SnapshotLetterheadAsync(template, employee, ct);
            var html = LetterHtml.ComposeDocument(subject, headerHtml, body, footerHtml, marginMm);

            if (System.Text.Encoding.UTF8.GetByteCount(html) > MaxHtmlBytes)
            {
                await MarkFailedAsync(letter, ct);
                return Error.Rule("letter.html_too_large", $"Rendered letter exceeds the {MaxHtmlBytes / 1024} KB limit.");
            }

            var pdfResult = await pdf.RenderPdfAsync(html, ct);
            if (!pdfResult.IsSuccess)
            {
                await MarkFailedAsync(letter, ct);
                return Error.Rule("letter.pdf_failed", pdfResult.Error?.Message ?? "PDF generation failed.");
            }

            var pdfBytes = pdfResult.Value;
            var sha = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();
            var fileName = $"{letter.LetterNumber}.pdf";

            var upload = await files.UploadAsync(new FileUploadRequest(
                EmployeeFoundationModule.IssuedLetterPurpose, fileName, "application/pdf",
                new MemoryStream(pdfBytes), pdfBytes.Length, "employee", employee.Id.ToString()), ct);
            if (!upload.IsSuccess)
            {
                await MarkFailedAsync(letter, ct);
                return Error.Rule("letter.file_failed", upload.Error?.Message ?? "Could not store the generated PDF.");
            }

            letter.Subject = subject;
            letter.RenderedText = body;
            letter.RenderedHtml = html;
            letter.FileId = upload.Value.Id;
            letter.PdfSha256 = sha;
            letter.SignatoryName = model["signatory_name"]?.ToString();
            letter.SignatoryDesignation = model["signatory_designation"]?.ToString();
            letter.LetterheadSnapshot = snapshotJson;
            letter.IssueDate = today;
            if (letter.SupersedesId is { } prevId)
            {
                var prev = await db.EmployeeLetters.FirstOrDefaultAsync(l => l.Id == prevId, ct);
                if (prev is not null && prev.Status == LetterStatuses.Issued)
                {
                    prev.Status = LetterStatuses.Superseded;
                }
            }
            letter.Status = LetterStatuses.Issued;
            await db.SaveChangesAsync(ct);
            return new IssueLetterResult(letter.Id, letter.LetterNumber, letter.FileId, letter.Status);
        }
        catch (Exception) // render/pdf/upload fault — preserve the record, keep the number consumed
        {
            await MarkFailedAsync(letter, ct);
            throw;
        }
    }

    private async Task MarkFailedAsync(EmployeeLetter letter, CancellationToken ct)
    {
        var tracked = await db.EmployeeLetters.FirstOrDefaultAsync(l => l.Id == letter.Id, ct);
        if (tracked is not null && tracked.Status == LetterStatuses.Issuing)
        {
            tracked.Status = LetterStatuses.GenerationFailed;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Retry a `generation_failed` letter — reuses its number and original template version.</summary>
    public async Task<Result<RetryLetterResult>> RetryAsync(Guid letterId, Guid actorId, CancellationToken ct)
    {
        var letter = await db.EmployeeLetters.FirstOrDefaultAsync(l => l.Id == letterId, ct);
        if (letter is null)
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.LettersIssue, letter.EmployeeId, ct))
        {
            return Error.NotFound("letter.not_found", "Letter not found.");
        }

        if (letter.Status != LetterStatuses.GenerationFailed)
        {
            return Error.Conflict("letter.retry_invalid", "Only letters with failed generation can be retried.");
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == letter.EmployeeId, ct);
        var type = await db.LetterTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == letter.LetterTypeId, ct);
        var template = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == letter.LetterTemplateId, ct);
        if (employee is null || type is null || template is null)
        {
            return Error.Rule("letter.retry_missing_refs", "Letter references a template or employee that no longer exists.");
        }

        var today = await TodayInBranchTzAsync(employee, ct);
        letter.Status = LetterStatuses.Issuing;
        await db.SaveChangesAsync(ct);

        var result = await GenerateAsync(letter, type, template, employee, today, ct);
        return !result.IsSuccess
            ? result.Error!
            : new RetryLetterResult(letter.Id, letter.LetterNumber, letter.FileId, LetterStatuses.Issued);
    }
}
