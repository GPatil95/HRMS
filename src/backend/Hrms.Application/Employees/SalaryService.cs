using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

public sealed record SalaryComponentInput(string Code, string Name, string Type, string? PayslipName,
    bool Taxable, bool ProRata, bool PartOfCtc, bool PartOfGross, bool IsBasic, int DisplayOrder, bool IsActive, uint? RowVersion = null);
public sealed record SalaryComponentDto(Guid Id, string Code, string Name, string Type, string? PayslipName,
    bool Taxable, bool ProRata, bool PartOfCtc, bool PartOfGross, bool IsStatutory, bool IsBasic, int DisplayOrder, bool IsActive, uint RowVersion);
public sealed record StructureLineInput(Guid SalaryComponentId, int Sequence, string CalcType, decimal? Amount, decimal? Percent, string? Formula);
public sealed record StructureLineDto(Guid Id, Guid SalaryComponentId, string ComponentCode, string ComponentName, string ComponentType,
    int Sequence, string CalcType, decimal? Amount, decimal? Percent, string? Formula);
public sealed record SalaryStructureInput(string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<StructureLineInput> Lines, uint? RowVersion = null);
public sealed record SalaryStructureDto(Guid Id, string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<StructureLineDto> Lines, uint RowVersion);
public sealed record SalaryAssignmentInput(Guid SalaryStructureId, decimal CtcAmount, string? Currency,
    DateOnly EffectiveFrom, string? Reason, bool Submit = false);
public sealed record SalaryAssignmentDto(Guid Id, Guid EmployeeId, Guid SalaryStructureId, string StructureName,
    decimal CtcAmount, string? Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Status, string? Reason,
    Guid? RequestedBy, DateTimeOffset RequestedAt, Guid? ApprovedBy, DateTimeOffset? ApprovedAt,
    IReadOnlyList<SalaryLineDto> Lines, uint RowVersion);
public sealed record SalaryLineDto(Guid SalaryComponentId, string ComponentCode, string ComponentName, string ComponentType,
    int Sequence, string CalcType, decimal? Amount, decimal? Percent, string? Formula, decimal? ResolvedAmount);
public sealed record RejectSalaryRequest(string? Reason);

/// <summary>
/// Salary masters (components, structures) and effective-dated employee salary assignments.
/// Assignments follow the lifecycle pattern: draft → submitted → approved →(effective date,
/// daily sweep or inline)→ active; the previous active row closes at the same date.
/// Resolved component lines are snapshotted at approval — the payroll-readiness artifact.
/// </summary>
public sealed class SalaryService(ITenantDbContext db, IClock clock)
{
    private static readonly string[] PendingStatuses =
        [SalaryAssignmentStatuses.Draft, SalaryAssignmentStatuses.Submitted, SalaryAssignmentStatuses.Approved];
    private static readonly string[] AppliedStatuses =
        [SalaryAssignmentStatuses.Active, SalaryAssignmentStatuses.Closed];
    // ---------- Components ----------

    public async Task<IReadOnlyList<SalaryComponentDto>> ListComponentsAsync(CancellationToken ct) =>
        await db.SalaryComponents.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Code)
            .Select(c => new SalaryComponentDto(c.Id, c.Code, c.Name, c.Type, c.PayslipName, c.Taxable, c.ProRata,
                c.PartOfCtc, c.PartOfGross, c.IsStatutory, c.IsBasic, c.DisplayOrder, c.IsActive, c.RowVersion))
            .ToListAsync(ct);

    public async Task<Result<Guid>> UpsertComponentAsync(SalaryComponentInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToLowerInvariant();
        var type = input.Type.Trim().ToLowerInvariant();
        if (code.Length is 0 or > 60 || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("component.invalid", "A component code (1-60 chars) and name are required.");
        }

        if (!SalaryComponentTypes.All.Contains(type))
        {
            return Error.Validation("component.type", $"Type must be one of: {string.Join(", ", SalaryComponentTypes.All)}.");
        }

        var existing = await db.SalaryComponents.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (existing is null)
        {
            var c = new SalaryComponent
            {
                Code = code, Name = input.Name.Trim(), Type = type, PayslipName = input.PayslipName?.Trim(),
                Taxable = input.Taxable, ProRata = input.ProRata, PartOfCtc = input.PartOfCtc,
                PartOfGross = input.PartOfGross, IsBasic = input.IsBasic,
                DisplayOrder = input.DisplayOrder, IsActive = input.IsActive,
            };
            db.SalaryComponents.Add(c);
            await db.SaveChangesAsync(ct);
            return c.Id;
        }

        if (input.RowVersion is { } rv && rv != existing.RowVersion)
        {
            return Error.Conflict("stale-version", "This record was modified elsewhere. Reload and retry.");
        }

        existing.Name = input.Name.Trim();
        existing.Type = type;
        existing.PayslipName = input.PayslipName?.Trim();
        existing.Taxable = input.Taxable;
        existing.ProRata = input.ProRata;
        existing.PartOfCtc = input.PartOfCtc;
        existing.PartOfGross = input.PartOfGross;
        existing.IsBasic = input.IsBasic;
        existing.DisplayOrder = input.DisplayOrder;
        existing.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    public async Task<Result> DeleteComponentAsync(Guid id, CancellationToken ct)
    {
        var c = await db.SalaryComponents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return Error.NotFound("component.not_found", "Salary component not found.");
        }

        if (c.IsStatutory)
        {
            return Error.Rule("component.statutory", "Statutory components cannot be deleted — deactivate them instead.");
        }

        if (await db.SalaryStructureLines.AnyAsync(l => l.SalaryComponentId == id, ct))
        {
            return Error.Conflict("component.in_use", "Component is used by a salary structure — remove the line first.");
        }

        db.SalaryComponents.Remove(c);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Structures ----------

    public async Task<IReadOnlyList<SalaryStructureDto>> ListStructuresAsync(CancellationToken ct)
    {
        var rows = await db.SalaryStructures.AsNoTracking().Include(s => s.Lines).OrderBy(s => s.Code).ToListAsync(ct);
        var componentIds = rows.SelectMany(s => s.Lines).Select(l => l.SalaryComponentId).Distinct().ToList();
        var components = await db.SalaryComponents.AsNoTracking().Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        return rows.Select(s => ToDto(s, components)).ToList();
    }

    public async Task<SalaryStructureDto?> GetStructureAsync(Guid id, CancellationToken ct)
    {
        var s = await db.SalaryStructures.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
        {
            return null;
        }

        var components = await db.SalaryComponents.AsNoTracking()
            .Where(c => s.Lines.Select(l => l.SalaryComponentId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return ToDto(s, components);
    }

    public async Task<Result<Guid>> UpsertStructureAsync(SalaryStructureInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToLowerInvariant();
        if (code.Length is 0 or > 60 || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("structure.invalid", "A structure code (1-60 chars) and name are required.");
        }

        var lineCheck = await ValidateLinesAsync(input.Lines, ct);
        if (!lineCheck.IsSuccess)
        {
            return lineCheck.Error!;
        }

        var s = await db.SalaryStructures.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Code == code, ct);
        if (s is null)
        {
            s = new SalaryStructure { Code = code };
            db.SalaryStructures.Add(s);
        }
        else
        {
            if (input.RowVersion is { } rv && rv != s.RowVersion)
            {
                return Error.Conflict("stale-version", "This record was modified elsewhere. Reload and retry.");
            }
        }

        s.Name = input.Name.Trim();
        s.Description = input.Description?.Trim();
        s.IsActive = input.IsActive;

        db.SalaryStructureLines.RemoveRange(s.Lines);
        s.Lines = input.Lines.Select(l => new SalaryStructureLine
        {
            SalaryStructureId = s.Id,
            SalaryComponentId = l.SalaryComponentId,
            Sequence = l.Sequence,
            CalcType = l.CalcType.Trim().ToLowerInvariant(),
            Amount = l.Amount,
            Percent = l.Percent,
            Formula = l.Formula?.Trim(),
        }).ToList();

        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    private async Task<Result> ValidateLinesAsync(IReadOnlyList<StructureLineInput> lines, CancellationToken ct)
    {
        if (lines.GroupBy(l => l.SalaryComponentId).Any(g => g.Count() > 1))
        {
            return Error.Validation("structure.duplicate", "Each component may appear only once per structure.");
        }

        foreach (var l in lines)
        {
            var calc = l.CalcType.Trim().ToLowerInvariant();
            if (!SalaryCalcTypes.All.Contains(calc))
            {
                return Error.Validation("structure.calc", $"Invalid calc type '{l.CalcType}'.");
            }

            if (calc == SalaryCalcTypes.Fixed && l.Amount is null)
            {
                return Error.Validation("structure.amount", "Fixed lines require an amount.");
            }

            if (calc is SalaryCalcTypes.PercentOfBasic or SalaryCalcTypes.PercentOfGross && l.Percent is null or <= 0)
            {
                return Error.Validation("structure.percent", "Percent lines require a positive percent.");
            }

            if (calc == SalaryCalcTypes.Formula && string.IsNullOrWhiteSpace(l.Formula))
            {
                return Error.Validation("structure.formula", "Formula lines require an expression.");
            }
        }

        var ids = lines.Select(l => l.SalaryComponentId).Distinct().ToList();
        var found = await db.SalaryComponents.CountAsync(c => ids.Contains(c.Id) && c.IsActive, ct);
        if (found != ids.Count)
        {
            return Error.Validation("structure.component", "One or more components are unknown or inactive.");
        }

        return Result.Success();
    }

    // ---------- Assignments ----------

    public async Task<IReadOnlyList<SalaryAssignmentDto>> ListAssignmentsAsync(Guid employeeId, CancellationToken ct)
    {
        var rows = await db.EmployeeSalaryAssignments.AsNoTracking().Include(a => a.Lines)
            .Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.EffectiveFrom).ToListAsync(ct);
        return await MapAsync(rows, ct);
    }

    public async Task<SalaryAssignmentDto?> CurrentAssignmentAsync(Guid employeeId, CancellationToken ct)
    {
        var row = await db.EmployeeSalaryAssignments.AsNoTracking().Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Status == SalaryAssignmentStatuses.Active, ct);
        return row is null ? null : (await MapAsync([row], ct)).First();
    }

    /// <summary>The employee's salary effective on a given date — the payroll/CTC-as-of read.</summary>
    public async Task<SalaryAssignmentDto?> AssignmentAsOfAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var row = await db.EmployeeSalaryAssignments.AsNoTracking().Include(a => a.Lines)
            .Where(a => AppliedStatuses.Contains(a.Status) &&
                        a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo > date))
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (await MapAsync([row], ct)).First();
    }

    public async Task<Result<SalaryAssignmentDto>> CreateAsync(Guid employeeId, SalaryAssignmentInput input, Guid requesterId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var structure = await db.SalaryStructures.AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == input.SalaryStructureId, ct);
        if (structure is null || !structure.IsActive)
        {
            return Error.Validation("structure.inactive", "Unknown or inactive salary structure.");
        }

        if (input.CtcAmount <= 0)
        {
            return Error.Validation("salary.ctc", "CTC amount must be positive.");
        }

        // Only one pending assignment at a time — approve, reject or cancel it first.
        var pending = await db.EmployeeSalaryAssignments.AnyAsync(a => a.EmployeeId == employeeId &&
            PendingStatuses.Contains(a.Status), ct);
        if (pending)
        {
            return Error.Conflict("salary.pending", "A pending salary assignment exists — approve, reject or cancel it first.");
        }

        var a = new EmployeeSalaryAssignment
        {
            EmployeeId = employeeId,
            SalaryStructureId = structure.Id,
            CtcAmount = input.CtcAmount,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "INR" : input.Currency.Trim().ToUpperInvariant(),
            EffectiveFrom = input.EffectiveFrom,
            Status = input.Submit ? SalaryAssignmentStatuses.Submitted : SalaryAssignmentStatuses.Draft,
            Reason = input.Reason?.Trim(),
            RequestedBy = requesterId,
            RequestedAt = clock.UtcNow,
        };
        db.EmployeeSalaryAssignments.Add(a);
        await db.SaveChangesAsync(ct);
        return (await MapAsync([a], ct)).First();
    }

    public async Task<Result> SubmitAsync(Guid assignmentId, CancellationToken ct) =>
        await TransitionAsync(assignmentId, [SalaryAssignmentStatuses.Draft], SalaryAssignmentStatuses.Submitted, "submit", ct);

    public async Task<Result<SalaryAssignmentDto>> ApproveAsync(Guid assignmentId, Guid approverId, CancellationToken ct)
    {
        var a = await db.EmployeeSalaryAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null)
        {
            return Error.NotFound("salary.not_found", "Salary assignment not found.");
        }

        if (a.Status != SalaryAssignmentStatuses.Submitted)
        {
            return Error.Conflict("salary.not_submitted", "Only a submitted assignment can be approved.");
        }

        if (a.RequestedBy == approverId)
        {
            return Error.Forbidden("salary.self_approval", "The submitter cannot approve their own salary assignment.");
        }

        a.Status = SalaryAssignmentStatuses.Approved;
        a.ApprovedBy = approverId;
        a.ApprovedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var today = DateOnly.FromDateTime(clock.UtcNow.DateTime);
        if (a.EffectiveFrom <= today)
        {
            var applied = await ActivateAsync(a, ct);
            if (!applied.IsSuccess)
            {
                return applied.Error!;
            }
        }

        var reloaded = await db.EmployeeSalaryAssignments.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == a.Id, ct);
        return (await MapAsync([reloaded], ct)).First();
    }

    public async Task<Result> RejectAsync(Guid assignmentId, Guid approverId, string? reason, CancellationToken ct)
    {
        var a = await db.EmployeeSalaryAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null)
        {
            return Error.NotFound("salary.not_found", "Salary assignment not found.");
        }

        if (a.Status != SalaryAssignmentStatuses.Submitted)
        {
            return Error.Conflict("salary.not_submitted", "Only a submitted assignment can be rejected.");
        }

        a.Status = SalaryAssignmentStatuses.Rejected;
        a.ApprovedBy = approverId;
        a.ApprovedAt = clock.UtcNow;
        a.Reason = reason ?? a.Reason;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid assignmentId, CancellationToken ct) =>
        await TransitionAsync(assignmentId,
            [SalaryAssignmentStatuses.Draft, SalaryAssignmentStatuses.Submitted, SalaryAssignmentStatuses.Approved],
            SalaryAssignmentStatuses.Cancelled, "cancel", ct);

    /// <summary>Daily sweep leg: activates approved assignments whose effective date arrived.</summary>
    public async Task<int> ActivateDueAssignmentsAsync(DateOnly today, CancellationToken ct)
    {
        var due = await db.EmployeeSalaryAssignments
            .Where(a => a.Status == SalaryAssignmentStatuses.Approved && a.EffectiveFrom <= today)
            .OrderBy(a => a.EffectiveFrom).ToListAsync(ct);
        foreach (var a in due)
        {
            await ActivateAsync(a, ct);
        }

        return due.Count;
    }

    /// <summary>Activates one approved assignment: closes the current active row at the same date
    /// and snapshots resolved component lines. Called from approval (past/today-dated) and the sweep.</summary>
    private async Task<Result> ActivateAsync(EmployeeSalaryAssignment a, CancellationToken ct)
    {
        if (a.Status != SalaryAssignmentStatuses.Approved)
        {
            return Result.Success();
        }

        var current = await db.EmployeeSalaryAssignments
            .FirstOrDefaultAsync(x => x.EmployeeId == a.EmployeeId && x.Status == SalaryAssignmentStatuses.Active && x.Id != a.Id, ct);
        if (current is not null)
        {
            if (a.EffectiveFrom <= current.EffectiveFrom)
            {
                return Error.Conflict("salary.overlap", "Effective date overlaps the current assignment's start.");
            }

            current.EffectiveTo = a.EffectiveFrom;
            current.Status = SalaryAssignmentStatuses.Closed;
        }

        var structure = await db.SalaryStructures.AsNoTracking().Include(s => s.Lines)
            .FirstAsync(s => s.Id == a.SalaryStructureId, ct);
        var components = await db.SalaryComponents.AsNoTracking()
            .Where(c => structure.Lines.Select(l => l.SalaryComponentId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var lines = ResolveLines(structure.Lines.OrderBy(l => l.Sequence).ToList(), components, a.CtcAmount);
        foreach (var l in lines)
        {
            l.EmployeeSalaryAssignmentId = a.Id;
            db.EmployeeSalaryLines.Add(l);
        }

        a.Status = SalaryAssignmentStatuses.Active;
        a.EffectiveTo = null;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Two-pass resolution: fixed amounts first; percent_of_basic resolves against the
    /// IsBasic component's fixed amount; percent_of_gross/formula stay unresolved for payroll.</summary>
    private static List<EmployeeSalaryLine> ResolveLines(List<SalaryStructureLine> lines,
        Dictionary<Guid, SalaryComponent> components, decimal annualCtc)
    {
        var result = lines.Select(l => new EmployeeSalaryLine
        {
            SalaryComponentId = l.SalaryComponentId,
            Sequence = l.Sequence,
            CalcType = l.CalcType,
            Amount = l.Amount,
            Percent = l.Percent,
            Formula = l.Formula,
            ResolvedAmount = l.CalcType == SalaryCalcTypes.Fixed ? l.Amount : null,
        }).ToList();

        var basicId = components.Values.FirstOrDefault(c => c.IsBasic)?.Id;
        var basicMonthly = basicId is null ? null : result.FirstOrDefault(r => r.SalaryComponentId == basicId)?.ResolvedAmount;
        foreach (var r in result.Where(r => r.CalcType == SalaryCalcTypes.PercentOfBasic && basicMonthly is not null))
        {
            r.ResolvedAmount = Math.Round(basicMonthly!.Value * r.Percent!.Value / 100m, 2);
        }

        return result;
    }

    private async Task<Result> TransitionAsync(Guid id, string[] from, string to, string verb, CancellationToken ct)
    {
        var a = await db.EmployeeSalaryAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null)
        {
            return Error.NotFound("salary.not_found", "Salary assignment not found.");
        }

        if (!from.Contains(a.Status))
        {
            return Error.Conflict($"salary.not_{verb}able", $"Cannot {verb} an assignment in status '{a.Status}'.");
        }

        a.Status = to;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<List<SalaryAssignmentDto>> MapAsync(List<EmployeeSalaryAssignment> rows, CancellationToken ct)
    {
        var structureIds = rows.Select(r => r.SalaryStructureId).Distinct().ToList();
        var structures = await db.SalaryStructures.AsNoTracking().Where(s => structureIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var componentIds = rows.SelectMany(r => r.Lines).Select(l => l.SalaryComponentId).Distinct().ToList();
        var components = await db.SalaryComponents.AsNoTracking().Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        return rows.Select(a => new SalaryAssignmentDto(a.Id, a.EmployeeId, a.SalaryStructureId,
            structures.GetValueOrDefault(a.SalaryStructureId, "?"), a.CtcAmount, a.Currency,
            a.EffectiveFrom, a.EffectiveTo, a.Status, a.Reason, a.RequestedBy, a.RequestedAt,
            a.ApprovedBy, a.ApprovedAt,
            a.Lines.OrderBy(l => l.Sequence).Select(l =>
            {
                var c = components.GetValueOrDefault(l.SalaryComponentId);
                return new SalaryLineDto(l.SalaryComponentId, c?.Code ?? "?", c?.Name ?? "?", c?.Type ?? "earning",
                    l.Sequence, l.CalcType, l.Amount, l.Percent, l.Formula, l.ResolvedAmount);
            }).ToList(),
            a.RowVersion)).ToList();
    }

    private static SalaryStructureDto ToDto(SalaryStructure s, Dictionary<Guid, SalaryComponent> components) =>
        new(s.Id, s.Code, s.Name, s.Description, s.IsActive,
            s.Lines.OrderBy(l => l.Sequence).Select(l =>
            {
                var c = components.GetValueOrDefault(l.SalaryComponentId);
                return new StructureLineDto(l.Id, l.SalaryComponentId, c?.Code ?? "?", c?.Name ?? "?", c?.Type ?? "earning",
                    l.Sequence, l.CalcType, l.Amount, l.Percent, l.Formula);
            }).ToList(),
            s.RowVersion);
}
