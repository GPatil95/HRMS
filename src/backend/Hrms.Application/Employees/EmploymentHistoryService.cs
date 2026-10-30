using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

/// <summary>
/// Employment-history primitives (design M2 §employment-history).
/// employees = current projection of the open history row; employment_history = truth.
/// Overlap safety is DB-enforced by the exclusion constraint — violations surface as 409, never silent drift.
/// </summary>
public sealed class EmploymentHistoryService(ITenantDbContext db)
{
    /// <summary>The open (current) history row — effective_to IS NULL.</summary>
    public Task<EmploymentHistory?> OpenRowAsync(Guid employeeId, CancellationToken ct) =>
        db.EmploymentHistory.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.EffectiveTo == null, ct);

    /// <summary>History row effective on a date — the as-of query used by payroll/leave later.</summary>
    public Task<EmploymentHistory?> AsOfAsync(Guid employeeId, DateOnly date, CancellationToken ct) =>
        db.EmploymentHistory
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo > date), ct);

    public Task<List<EmploymentHistory>> ListAsync(Guid employeeId, CancellationToken ct) =>
        db.EmploymentHistory.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);

    /// <summary>
    /// Applies a period transition: closes the current open row at effectiveFrom (must be >= open row's from,
    /// except backdated transitions which re-range), inserts the supplied row, reprojects.
    /// Overlap violations from the exclusion constraint are caught by the caller's DbUpdateException handler.
    /// </summary>
    public async Task<Result> ApplyTransitionAsync(Employee employee, EmploymentHistory next, CancellationToken ct)
    {
        var open = await OpenRowAsync(employee.Id, ct);
        if (open is not null)
        {
            if (next.EffectiveFrom <= open.EffectiveFrom)
            {
                return Error.Conflict("history-overlap",
                    $"Effective date {next.EffectiveFrom} overlaps the open period starting {open.EffectiveFrom}. Use a correction event for backdated changes.");
            }

            open.EffectiveTo = next.EffectiveFrom;
        }

        next.EmployeeId = employee.Id;
        next.EffectiveTo = null;
        db.EmploymentHistory.Add(next);
        Reproject(employee, next);
        return Result.Success();
    }

    /// <summary>Copies the given (usually open) history row's assignment fields onto the employee projection.</summary>
    public static void Reproject(Employee employee, EmploymentHistory current)
    {
        employee.CompanyId = current.CompanyId;
        employee.BranchId = current.BranchId;
        employee.DepartmentId = current.DepartmentId;
        employee.DesignationId = current.DesignationId;
        employee.GradeId = current.GradeId;
        employee.LocationId = current.LocationId;
        employee.CostCentreId = current.CostCentreId;
        employee.ManagerEmployeeId = current.ManagerEmployeeId;
        employee.EmploymentType = current.EmploymentType;
    }

    /// <summary>Rebuilds the employee projection from the DB's open row (drift repair + post-correction).</summary>
    public async Task<Result> ReprojectAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct);
        if (employee is null)
        {
            return Error.NotFound("employee-not-found", "Employee not found.");
        }

        var open = await OpenRowAsync(employeeId, ct);
        if (open is null)
        {
            return Error.Conflict("no-open-history", "Employee has no open employment period — cannot reproject.");
        }

        Reproject(employee, open);
        return Result.Success();
    }
}
