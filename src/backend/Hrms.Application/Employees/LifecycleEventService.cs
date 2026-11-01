using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

/// <summary>Typed lifecycle-event payload (jsonb on lifecycle_events). Fields are nullable so each event
/// type carries only what it changes; the apply path merges them onto the employee's open history row.</summary>
public sealed record LifecyclePayload(
    Guid? CompanyId = null,
    Guid? BranchId = null,
    Guid? DepartmentId = null,
    Guid? DesignationId = null,
    Guid? GradeId = null,
    Guid? LocationId = null,
    Guid? CostCentreId = null,
    Guid? ManagerEmployeeId = null,
    string? EmploymentType = null,
    DateOnly? JoiningDate = null,
    DateOnly? ProbationEndDate = null,
    DateOnly? ConfirmationDate = null,
    DateOnly? LastWorkingDate = null,
    string? ExitReason = null);

public sealed record LifecycleEventInput(
    string EventType,
    DateOnly EffectiveDate,
    string? Reason,
    LifecyclePayload? Payload,
    bool Submit = true);

public sealed record LifecycleEventDto(
    Guid Id, Guid EmployeeId, string EventType, string Status, DateOnly EffectiveDate,
    string? Reason, Guid RequestedBy, DateTimeOffset RequestedAt,
    Guid? ApprovedBy, DateTimeOffset? ApprovedAt, Guid? RejectedBy, string? RejectionReason,
    DateTimeOffset? EffectiveAt, uint RowVersion);

/// <summary>Lifecycle-event engine: create/submit/approve/reject/cancel + materialize approved events into
/// employment-history transitions. SoD: approver must differ from the submitter. Past-dated approvals apply
/// immediately; future-dated ones wait for the daily tenant-timezone activation sweep.</summary>
public sealed class LifecycleEventService(ITenantDbContext db, EmploymentHistoryService history, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<LifecycleEventDto>> CreateAsync(Guid employeeId, LifecycleEventInput input, Guid requesterId, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return Error.NotFound("employee.not_found", "Employee not found.");
        }

        var eventType = input.EventType.Trim().ToLowerInvariant();
        if (!LifecycleEventTypes.All.Contains(eventType, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation("event.invalid_type", $"Unknown lifecycle event type '{input.EventType}'.");
        }

        if (eventType is LifecycleEventTypes.Joining && employee.Status != EmployeeStatus.PreJoining)
        {
            return Error.Conflict("event.joining_not_allowed", "A joining event only applies to a pre-joining employee.");
        }

        if (eventType is not LifecycleEventTypes.Joining && employee.Status is EmployeeStatus.PreJoining)
        {
            return Error.Conflict("event.prejoining", "Only a joining event may run while the employee is pre-joining.");
        }

        if (eventType is LifecycleEventTypes.Exit && employee.Status is EmployeeStatus.Exited)
        {
            return Error.Conflict("event.already_exited", "The employee has already exited.");
        }

        var payload = input.Payload ?? new LifecyclePayload();
        var validation = await ValidatePayloadAsync(employeeId, eventType, payload, ct);
        if (validation is not null)
        {
            return validation;
        }

        var now = clock.UtcNow;
        var entity = new LifecycleEvent
        {
            EmployeeId = employeeId,
            EventType = eventType,
            Status = input.Submit ? LifecycleEventStatus.Submitted : LifecycleEventStatus.Draft,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            EffectiveDate = input.EffectiveDate,
            Reason = input.Reason,
            RequestedBy = requesterId,
            RequestedAt = now,
        };
        db.LifecycleEvents.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<Result<LifecycleEventDto>> SubmitAsync(Guid eventId, Guid requesterId, CancellationToken ct)
    {
        var e = await FindAsync(eventId, ct);
        if (e is null)
        {
            return Error.NotFound("event.not_found", "Lifecycle event not found.");
        }

        if (e.Status != LifecycleEventStatus.Draft)
        {
            return Error.Conflict("event.not_draft", "Only a draft event can be submitted.");
        }

        e.Status = LifecycleEventStatus.Submitted;
        e.RequestedBy = requesterId;
        e.RequestedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    public async Task<Result<LifecycleEventDto>> ApproveAsync(Guid eventId, Guid approverId, CancellationToken ct)
    {
        var e = await FindAsync(eventId, ct);
        if (e is null)
        {
            return Error.NotFound("event.not_found", "Lifecycle event not found.");
        }

        if (e.Status != LifecycleEventStatus.Submitted)
        {
            return Error.Conflict("event.not_submitted", "Only a submitted event can be approved.");
        }

        if (e.RequestedBy == approverId)
        {
            return Error.Forbidden("event.self_approval", "The submitter cannot approve their own event.");
        }

        var today = await TodayAsync(e.EmployeeId, ct);
        if (e.EffectiveDate < today && e.EventType != LifecycleEventTypes.Correction)
        {
            var open = await history.OpenRowAsync(e.EmployeeId, ct);
            if (open is not null && e.EffectiveDate <= open.EffectiveFrom)
            {
                return Error.Conflict("event.backdated", "Effective date overlaps the current employment period; use a correction event.");
            }
        }

        e.Status = LifecycleEventStatus.Approved;
        e.ApprovedBy = approverId;
        e.ApprovedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        if (e.EffectiveDate <= today)
        {
            var applied = await ApplyAsync(e, ct);
            if (applied is not null)
            {
                return applied;
            }
        }

        return ToDto(e);
    }

    public async Task<Result<LifecycleEventDto>> RejectAsync(Guid eventId, Guid approverId, string? reason, CancellationToken ct)
    {
        var e = await FindAsync(eventId, ct);
        if (e is null)
        {
            return Error.NotFound("event.not_found", "Lifecycle event not found.");
        }

        if (e.Status != LifecycleEventStatus.Submitted)
        {
            return Error.Conflict("event.not_submitted", "Only a submitted event can be rejected.");
        }

        if (e.RequestedBy == approverId)
        {
            return Error.Forbidden("event.self_approval", "The submitter cannot reject their own event.");
        }

        e.Status = LifecycleEventStatus.Rejected;
        e.RejectedBy = approverId;
        e.RejectionReason = reason;
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    public async Task<Result<LifecycleEventDto>> CancelAsync(Guid eventId, Guid requesterId, CancellationToken ct)
    {
        var e = await FindAsync(eventId, ct);
        if (e is null)
        {
            return Error.NotFound("event.not_found", "Lifecycle event not found.");
        }

        if (e.Status is not (LifecycleEventStatus.Draft or LifecycleEventStatus.Submitted or LifecycleEventStatus.Approved))
        {
            return Error.Conflict("event.not_cancellable", "Only a draft, submitted or approved (not yet effective) event can be cancelled.");
        }

        e.Status = LifecycleEventStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    /// <summary>Applies one approved event whose effective date has arrived. Idempotent: already-effective events return null.</summary>
    public async Task<Result<LifecycleEventDto>?> ApplyAsync(LifecycleEvent e, CancellationToken ct)
    {
        if (e.Status != LifecycleEventStatus.Approved)
        {
            return null;
        }

        var employee = await db.Employees.FirstAsync(x => x.Id == e.EmployeeId, ct);
        var payload = JsonSerializer.Deserialize<LifecyclePayload>(e.PayloadJson, Json) ?? new LifecyclePayload();
        var open = await history.OpenRowAsync(e.EmployeeId, ct);
        var today = await TodayAsync(e.EmployeeId, ct);

        if (e.EventType == LifecycleEventTypes.Exit)
        {
            if (open is not null)
            {
                open.EffectiveTo = e.EffectiveDate;
                if (payload.LastWorkingDate is { } lastWorking)
                {
                    open.EffectiveTo = lastWorking;
                }
            }

            employee.Status = EmployeeStatus.Exited;
        }
        else
        {
            var next = new EmploymentHistory
            {
                EmployeeId = e.EmployeeId,
                CompanyId = payload.CompanyId ?? open?.CompanyId ?? employee.CompanyId,
                BranchId = payload.BranchId ?? open?.BranchId ?? employee.BranchId,
                DepartmentId = payload.DepartmentId ?? open?.DepartmentId ?? employee.DepartmentId,
                DesignationId = payload.DesignationId ?? open?.DesignationId ?? employee.DesignationId,
                GradeId = payload.GradeId ?? open?.GradeId ?? employee.GradeId,
                LocationId = payload.LocationId ?? open?.LocationId ?? employee.LocationId,
                CostCentreId = payload.CostCentreId ?? open?.CostCentreId ?? employee.CostCentreId,
                ManagerEmployeeId = payload.ManagerEmployeeId ?? open?.ManagerEmployeeId ?? employee.ManagerEmployeeId,
                EmploymentType = payload.EmploymentType ?? open?.EmploymentType ?? employee.EmploymentType ?? EmploymentTypes.Permanent,
                EventType = e.EventType,
                EffectiveFrom = e.EffectiveDate,
                Reason = e.Reason,
                PayrollAffecting = e.EffectiveDate < today,
                LifecycleEventId = e.Id,
            };

            // A joining event on the same day the employee was registered closes the
            // initial_assignment row to a zero-length period (empty daterange — no overlap).
            if (e.EventType == LifecycleEventTypes.Joining && open?.EventType == LifecycleEventTypes.InitialAssignment && e.EffectiveDate <= open.EffectiveFrom)
            {
                open.EffectiveTo = e.EffectiveDate;
                next.EffectiveTo = null;
                db.EmploymentHistory.Add(next);
                EmploymentHistoryService.Reproject(employee, next);
            }
            else
            {
                var applied = await history.ApplyTransitionAsync(employee, next, ct);
                if (!applied.IsSuccess)
                {
                    e.Status = LifecycleEventStatus.Submitted; // release for correction/re-approval
                    e.ApprovedBy = null;
                    e.ApprovedAt = null;
                    await db.SaveChangesAsync(ct);
                    return applied.Error!;
                }
            }

            if (e.EventType == LifecycleEventTypes.Joining && employee.Status == EmployeeStatus.PreJoining)
            {
                employee.Status = EmployeeStatus.Active;
                if (payload.JoiningDate is { } joining)
                {
                    employee.JoiningDate = joining;
                }

                if (payload.ProbationEndDate is { } probation)
                {
                    employee.ProbationEndDate = probation;
                }
            }

            if (e.EventType == LifecycleEventTypes.Confirmation && payload.ConfirmationDate is { } confirm)
            {
                employee.ConfirmationDate = confirm;
            }

            if (e.EventType is LifecycleEventTypes.Confirmation or LifecycleEventTypes.Promotion && employee.Status == EmployeeStatus.PreJoining)
            {
                employee.Status = EmployeeStatus.Active;
            }
        }

        e.Status = LifecycleEventStatus.Effective;
        e.EffectiveAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Daily activation sweep: applies every approved event whose effective date has arrived.
    /// The caller supplies 'today' (tenant-local date resolved by the scheduling layer).</summary>
    public async Task<int> ActivateDueEventsAsync(DateOnly today, CancellationToken ct)
    {
        var due = await db.LifecycleEvents
            .Where(e => e.Status == LifecycleEventStatus.Approved && e.EffectiveDate <= today)
            .OrderBy(e => e.EffectiveDate)
            .ToListAsync(ct);

        var applied = 0;
        foreach (var e in due)
        {
            if (await ApplyAsync(e, ct) is null)
            {
                applied++;
            }
        }

        return applied;
    }

    public async Task<IReadOnlyList<LifecycleEventDto>> ListAsync(Guid employeeId, CancellationToken ct)
    {
        var rows = await db.LifecycleEvents.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.RequestedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<LifecycleEventDto?> GetAsync(Guid eventId, CancellationToken ct)
    {
        var e = await db.LifecycleEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        return e is null ? null : ToDto(e);
    }

    private Task<LifecycleEvent?> FindAsync(Guid eventId, CancellationToken ct) =>
        db.LifecycleEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);

    /// <summary>'Today' in the employee's branch timezone (company events are branch-local for HR purposes).</summary>
    private async Task<DateOnly> TodayAsync(Guid employeeId, CancellationToken ct)
    {
        var tzName = await db.Employees.Where(e => e.Id == employeeId)
            .Select(e => db.Branches.Where(b => b.Id == e.BranchId).Select(b => b.TimeZone).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
        TimeZoneInfo tz;
        try
        {
            tz = tzName is not null ? TimeZoneInfo.FindSystemTimeZoneById(tzName) : TimeZoneInfo.Utc;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.Utc;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, tz).DateTime);
    }

    private async Task<Error?> ValidatePayloadAsync(Guid employeeId, string eventType, LifecyclePayload p, CancellationToken ct)
    {
        async Task<Error?> RefExists(Guid? id, Func<Guid, Task<bool>> check, string code, string label)
        {
            if (id is null) return null;
            return await check(id.Value) ? null : Error.Validation(code, $"{label} does not exist or is inactive.");
        }

        if (await RefExists(p.BranchId, id => db.Branches.AnyAsync(b => b.Id == id && b.IsActive, ct), "ref.branch", "Branch") is { } br) return br;
        if (await RefExists(p.DepartmentId, id => db.Departments.AnyAsync(d => d.Id == id && d.IsActive, ct), "ref.department", "Department") is { } dep) return dep;
        if (await RefExists(p.DesignationId, id => db.Designations.AnyAsync(d => d.Id == id && d.IsActive, ct), "ref.designation", "Designation") is { } des) return des;
        if (await RefExists(p.GradeId, id => db.Grades.AnyAsync(g => g.Id == id && g.IsActive, ct), "ref.grade", "Grade") is { } gr) return gr;
        if (await RefExists(p.LocationId, id => db.WorkLocations.AnyAsync(l => l.Id == id && l.IsActive, ct), "ref.location", "Work location") is { } loc) return loc;
        if (await RefExists(p.CostCentreId, id => db.CostCentres.AnyAsync(c => c.Id == id && c.IsActive, ct), "ref.cost_centre", "Cost centre") is { } cc) return cc;
        if (await RefExists(p.ManagerEmployeeId, id => db.Employees.AnyAsync(m => m.Id == id && m.Status != EmployeeStatus.Exited && m.Id != employeeId, ct), "ref.manager", "Manager") is { } mgr) return mgr;
        if (p.EmploymentType is { } et && !EmploymentTypes.All.Contains(et))
        {
            return Error.Validation("ref.employment_type", $"Unknown employment type '{et}'.");
        }

        return null;
    }

    private static LifecycleEventDto ToDto(LifecycleEvent e) => new(
        e.Id, e.EmployeeId, e.EventType, e.Status.ToString().ToLowerInvariant(), e.EffectiveDate,
        e.Reason, e.RequestedBy, e.RequestedAt, e.ApprovedBy, e.ApprovedAt,
        e.RejectedBy, e.RejectionReason, e.EffectiveAt, e.RowVersion);
}
