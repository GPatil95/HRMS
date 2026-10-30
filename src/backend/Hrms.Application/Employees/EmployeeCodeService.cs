using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

/// <summary>
/// Concurrency-safe employee-code generation (design M2 §employee-code).
/// Series key = resolved prefix, so companies sharing a prefix share one series; codes are never reused.
/// </summary>
public sealed class EmployeeCodeService(ITenantDbContext db)
{
    public static string SeriesKey(string prefix) => prefix.Trim().ToUpperInvariant();

    /// <summary>Resolves the numbering config for a company (tenant default when no company row exists).</summary>
    public async Task<EmployeeNumberingConfig> ResolveConfigAsync(Guid? companyId, CancellationToken ct)
    {
        var cfg = companyId.HasValue
            ? await db.EmployeeNumberingConfigs.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.IsActive, ct)
            : null;
        cfg ??= await db.EmployeeNumberingConfigs.FirstOrDefaultAsync(x => x.CompanyId == null && x.IsActive, ct);
        return cfg ?? new EmployeeNumberingConfig { Prefix = "EMP", PadWidth = 5, AllowManual = true };
    }

    /// <summary>
    /// Lazy-init + atomic increment in one round-trip flow: INSERT … ON CONFLICT DO NOTHING then
    /// UPDATE … RETURNING next_value - 1. Safe under concurrent creates — the second waiter serializes on the row.
    /// </summary>
    public async Task<string> NextCodeAsync(string prefix, int padWidth, CancellationToken ct)
    {
        var key = SeriesKey(prefix);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO org.employee_code_series (series_key, next_value) VALUES ({key}, 1) ON CONFLICT (series_key) DO NOTHING", ct);
        long value = 0;
        await foreach (var v in db.Database.SqlQuery<long>(
            $"UPDATE org.employee_code_series SET next_value = next_value + 1 WHERE series_key = {key} RETURNING next_value - 1 AS \"Value\"")
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            value = v;
        }
        return $"{key}{value.ToString().PadLeft(Math.Clamp(padWidth, 1, 12), '0')}";
    }

    /// <summary>Manual code entry — validates uniqueness; allowed only when config permits.</summary>
    public async Task<Result<string>> ValidateManualCodeAsync(EmployeeNumberingConfig cfg, string code, CancellationToken ct)
    {
        if (!cfg.AllowManual)
        {
            return Error.Validation("manual-code-disabled", "Manual employee codes are disabled for this company.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 32)
        {
            return Error.Validation("invalid-code", "Employee code must be 2-32 characters.");
        }

        return await db.Employees.AnyAsync(x => x.EmployeeCode == normalized, ct)
            ? Error.Conflict("code-taken", $"Employee code '{normalized}' is already in use.")
            : normalized;
    }
}
