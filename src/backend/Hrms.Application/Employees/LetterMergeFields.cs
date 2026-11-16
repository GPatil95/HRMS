using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

/// <summary>A merge field usable inside letter template subjects/bodies (Scriban syntax {{ field }}).</summary>
public sealed record LetterMergeFieldDto(string Name, string Description, bool RequiresSalaryPermission);

/// <summary>
/// Merge-field catalogue and model builder shared by preview and issuance. Any field flagged
/// salary-gated requires the caller to hold the salary-view permission on the employee; template
/// validation on publish rejects gated fields outright (they are reserved for Payroll).
/// </summary>
public static class LetterMergeFields
{
    public static readonly IReadOnlyList<LetterMergeFieldDto> Catalogue =
    [
        new("employee_code", "Employee code", false),
        new("display_name", "Employee display name", false),
        new("first_name", "Employee first name", false),
        new("last_name", "Employee last name", false),
        new("work_email", "Employee work email", false),
        new("employment_type", "Employment type (Permanent, …)", false),
        new("joining_date", "Date of joining (dd MMM yyyy)", false),
        new("company_name", "Company name", false),
        new("branch_name", "Branch name", false),
        new("department_name", "Current department", false),
        new("designation_name", "Current designation", false),
        new("signatory_name", "Signatory name", false),
        new("signatory_designation", "Signatory designation", false),
        new("letter_type", "Letter type name", false),
        new("letter_number", "Issued letter number (empty in preview)", false),
        new("letter_date", "Issue date (dd MMM yyyy)", false),
        new("date", "Today (dd MMM yyyy)", false),
        // Salary fields are intentionally absent: deferred to Payroll readiness. Publishing a
        // template that references {{ salary_* }} is rejected by validation.
    ];

    /// <summary>Extracts {{ name }} identifiers referenced by a template string.</summary>
    public static IReadOnlyList<string> ReferencedFields(string? template)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            return names;
        }

        var span = template.AsSpan();
        var i = 0;
        while (i + 1 < span.Length)
        {
            var open = span.Slice(i).IndexOf("{{", StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            var close = span.Slice(i + open + 2).IndexOf("}}", StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            var inner = span.Slice(i + open + 2, close).ToString().Trim();
            // Take the leading identifier of e.g. "employee_code", "if x", "employee_code | date..."
            var ident = inner.Split(' ', '|', '(')[0].Trim();
            if (ident.StartsWith("if ") || ident is "end" or "else" or "end_if" or "end_for" or "for")
            {
                i += open + 2 + close + 2;
                continue;
            }

            if (ident.StartsWith("salary", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(ident);
            }
            else if (Catalogue.Any(f => f.Name.Equals(ident, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(ident);
            }
            else if (!names.Contains(ident))
            {
                names.Add(ident); // unknown — reported by validation
            }

            i += open + 2 + close + 2;
        }

        return names;
    }

    /// <summary>
    /// Builds the render model for preview/issue. `letterNumber` is empty for preview.
    /// Signatory: new SignatoryId master wins; legacy SignatoryEmployeeId remains as dual-read fallback.
    /// </summary>
    public static async Task<Dictionary<string, object?>> BuildAsync(
        ITenantDbContext db, IClock clock, Employee e, LetterType type, string? letterNumber, DateOnly today, CancellationToken ct)
    {
        var open = await db.EmploymentHistory.AsNoTracking().FirstOrDefaultAsync(h => h.EmployeeId == e.Id && h.EffectiveTo == null, ct);
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == e.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var branch = await db.Branches.AsNoTracking().Where(b => b.Id == e.BranchId).Select(b => b.Name).FirstOrDefaultAsync(ct);
        var department = open?.DepartmentId is { } dep ? await db.Departments.AsNoTracking().Where(d => d.Id == dep).Select(d => d.Name).FirstOrDefaultAsync(ct) : null;
        var designation = open?.DesignationId is { } des ? await db.Designations.AsNoTracking().Where(d => d.Id == des).Select(d => d.Name).FirstOrDefaultAsync(ct) : null;

        string? signatoryName = null, signatoryDesignation = null;
        if (type.SignatoryId is { } sid)
        {
            var row = await db.LetterSignatories.AsNoTracking().Where(s => s.Id == sid).Select(s => new { s.Name, s.Designation }).FirstOrDefaultAsync(ct);
            signatoryName = row?.Name;
            signatoryDesignation = row?.Designation;
        }
        else if (type.SignatoryEmployeeId is { } legacySid)
        {
            signatoryName = await db.Employees.AsNoTracking().Where(s => s.Id == legacySid).Select(s => s.DisplayName).FirstOrDefaultAsync(ct);
            signatoryDesignation = await db.EmploymentHistory.AsNoTracking()
                .Where(h => h.EmployeeId == legacySid && h.EffectiveTo == null && h.DesignationId != null)
                .Select(h => db.Designations.Where(d => d.Id == h.DesignationId).Select(d => d.Name).FirstOrDefault())
                .FirstOrDefaultAsync(ct);
        }

        var fmt = "dd MMM yyyy";
        return new Dictionary<string, object?>
        {
            ["employee_code"] = e.EmployeeCode,
            ["display_name"] = e.DisplayName,
            ["first_name"] = e.FirstName ?? e.DisplayName,
            ["last_name"] = e.LastName ?? string.Empty,
            ["work_email"] = e.WorkEmail ?? string.Empty,
            ["employment_type"] = e.EmploymentType ?? EmploymentTypes.Permanent,
            ["joining_date"] = e.JoiningDate?.ToString(fmt) ?? string.Empty,
            ["company_name"] = company ?? string.Empty,
            ["branch_name"] = branch ?? string.Empty,
            ["department_name"] = department ?? string.Empty,
            ["designation_name"] = designation ?? string.Empty,
            ["signatory_name"] = signatoryName ?? string.Empty,
            ["signatory_designation"] = signatoryDesignation ?? string.Empty,
            ["letter_type"] = type.Name,
            ["letter_number"] = letterNumber ?? string.Empty,
            ["letter_date"] = today.ToString(fmt),
            ["date"] = today.ToString(fmt),
        };
    }
}
