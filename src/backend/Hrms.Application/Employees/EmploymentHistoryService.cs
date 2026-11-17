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
        if (open is not null && next.EffectiveFrom <= open.EffectiveFrom)
        {
            return Error.Conflict("history-overlap",
                $"Effective date {next.EffectiveFrom} overlaps the open period starting {open.EffectiveFrom}. Use a correction event for backdated changes.");
        }

        // Rebuild the closure subtree BEFORE touching history rows so a rejected
        // move leaves nothing staged on the change tracker.
        var previousManager = open?.ManagerEmployeeId ?? employee.ManagerEmployeeId;
        if (next.ManagerEmployeeId != previousManager)
        {
            var moved = await MoveSubtreeAsync(employee.Id, next.ManagerEmployeeId, ct);
            if (!moved.IsSuccess)
            {
                return moved.Error!;
            }
        }

        if (open is not null)
        {
            open.EffectiveTo = next.EffectiveFrom;
        }

        next.EmployeeId = employee.Id;
        next.EffectiveTo = null;
        db.EmploymentHistory.Add(next);
        Reproject(employee, next);
        return Result.Success();
    }

    /// <summary>
    /// Closure-table subtree move for a manager change: detaches every link between the employee's
    /// ancestors and the employee's whole subtree, then re-links the subtree under the new manager's
    /// ancestor chain. The self row (depth 0) and intra-subtree rows are untouched.
    /// Rejects a new manager inside the employee's own subtree (cycle).
    /// </summary>
    public async Task<Result> MoveSubtreeAsync(Guid employeeId, Guid? newManagerId, CancellationToken ct)
    {
        if (newManagerId == employeeId)
        {
            return Error.Validation("manager.self", "An employee cannot report to themselves.");
        }

        var subtree = await db.ReportingClosure
            .Where(c => c.AncestorId == employeeId)
            .ToListAsync(ct); // (employeeId, d, depth E→d), includes the self row

        if (newManagerId is { } m && subtree.Any(c => c.DescendantId == m && c.Depth > 0))
        {
            return Error.Conflict("manager.cycle", "The selected manager is inside this employee's reporting subtree — the change would create a cycle.");
        }

        var subtreeIds = subtree.Select(c => c.DescendantId).ToHashSet();

        // Old upward links: rows (a, d) where d ∈ subtree and a is an ancestor ABOVE the employee
        // (a ∉ subtree — ancestors within the subtree are intra-subtree paths we keep).
        var oldLinks = await db.ReportingClosure
            .Where(c => subtreeIds.Contains(c.DescendantId) && !subtreeIds.Contains(c.AncestorId))
            .ToListAsync(ct);
        db.ReportingClosure.RemoveRange(oldLinks);

        if (newManagerId is { } managerId)
        {
            var managerAncestors = await db.ReportingClosure.AsNoTracking()
                .Where(c => c.DescendantId == managerId)
                .ToListAsync(ct); // (a, managerId, depth a→manager), includes self row
            foreach (var a in managerAncestors)
            {
                foreach (var d in subtree)
                {
                    db.ReportingClosure.Add(new ReportingClosure
                    {
                        AncestorId = a.AncestorId,
                        DescendantId = d.DescendantId,
                        Depth = a.Depth + 1 + d.Depth,
                    });
                }
            }
        }

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
