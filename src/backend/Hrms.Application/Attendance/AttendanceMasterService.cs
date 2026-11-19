using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Attendance;

/// <summary>CRUD + effective-dated version lifecycle for attendance masters (M3.1).</summary>
public sealed class AttendanceMasterService(ITenantDbContext db, IClock clock, IDataScopeService scopes)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---------- Shifts ----------

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct)
    {
        var today = Today;
        var shifts = await db.Shifts.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        var versions = await db.ShiftVersions.AsNoTracking()
            .Where(v => v.Status == AttendanceVersionStatuses.Published).ToListAsync(ct);
        var counts = await db.ShiftVersions.GroupBy(v => v.ShiftId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return shifts.Select(s =>
        {
            var active = versions.Where(v => v.ShiftId == s.Id && v.EffectiveFrom <= today && (v.EffectiveTo == null || v.EffectiveTo >= today))
                .OrderByDescending(v => v.EffectiveFrom).FirstOrDefault();
            return s.ToDto(counts.FirstOrDefault(c => c.Key == s.Id)?.Count ?? 0, active);
        }).ToList();
    }

    public async Task<Result<ShiftDto>> CreateShiftAsync(ShiftInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 32)
            return Error.Validation("shift.code", "Code is required (max 32 chars).");
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.Shifts.AnyAsync(s => s.Code == code, ct))
            return Error.Conflict("shift.duplicate_code", $"Shift code '{code}' already exists.");
        var shift = new Shift { Code = code, Name = input.Name?.Trim() ?? code, IsActive = input.IsActive };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return shift.ToDto(0, null);
    }

    public async Task<Result> UpdateShiftAsync(Guid id, ShiftInput input, CancellationToken ct)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shift is null) return Error.NotFound("shift.not_found", "Shift not found.");
        if (!string.Equals(shift.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase)
            && await db.Shifts.AnyAsync(s => s.Code == input.Code.Trim(), ct))
            return Error.Conflict("shift.duplicate_code", $"Shift code '{input.Code}' already exists.");
        shift.Code = input.Code.Trim().ToUpperInvariant();
        shift.Name = input.Name.Trim();
        shift.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ShiftVersionDto>>> ListShiftVersionsAsync(Guid shiftId, CancellationToken ct)
    {
        if (!await db.Shifts.AnyAsync(s => s.Id == shiftId, ct))
            return Error.NotFound("shift.not_found", "Shift not found.");
        return Result<IReadOnlyList<ShiftVersionDto>>.Ok(
            (await db.ShiftVersions.AsNoTracking().Where(v => v.ShiftId == shiftId)
                .OrderByDescending(v => v.Version).ToListAsync(ct)).Select(v => v.ToDto()).ToList());
    }

    public async Task<Result<ShiftVersionDto>> CreateShiftVersionAsync(Guid shiftId, ShiftVersionInput input, CancellationToken ct)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
        if (shift is null) return Error.NotFound("shift.not_found", "Shift not found.");
        var error = ValidateShiftVersion(input);
        if (error is not null) return error;
        var next = await db.ShiftVersions.Where(v => v.ShiftId == shiftId).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = new ShiftVersion
        {
            ShiftId = shiftId, Version = next + 1,
            EffectiveFrom = Today.AddDays(1), // drafts carry a suggested window; publish sets the real one
        };
        ApplyShiftVersionInput(version, input);
        db.ShiftVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return version.ToDto();
    }

    public async Task<Result> UpdateShiftVersionAsync(Guid versionId, ShiftVersionInput input, CancellationToken ct)
    {
        var version = await db.ShiftVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("shift_version.not_found", "Shift version not found.");
        if (version.Status != AttendanceVersionStatuses.Draft)
            return Error.Conflict("shift_version.immutable", $"A {version.Status} version is immutable — create a new draft.");
        var error = ValidateShiftVersion(input);
        if (error is not null) return error;
        ApplyShiftVersionInput(version, input);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("shift_version.immutable", "The version was published or retired concurrently — create a new draft.");
        }
        return Result.Success();
    }

    public async Task<Result<ShiftVersionDto>> PublishShiftVersionAsync(
        Guid versionId, DateOnly effectiveFrom, bool canBackdate, CancellationToken ct)
    {
        var version = await db.ShiftVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("shift_version.not_found", "Shift version not found.");
        if (version.Status != AttendanceVersionStatuses.Draft)
            return Error.Conflict("shift_version.not_draft", $"Only draft versions can be published (current: {version.Status}).");
        if (effectiveFrom < Today && !canBackdate)
            return Error.Forbidden("shift_version.backdate_forbidden", "Backdated publication requires the attendance.masters.backdate permission.");
        var overlap = await db.ShiftVersions
            .Where(v => v.ShiftId == version.ShiftId && v.Status == AttendanceVersionStatuses.Published
                && (v.EffectiveTo == null || v.EffectiveTo >= effectiveFrom))
            .OrderBy(v => v.EffectiveFrom).ToListAsync(ct);
        // Close the previous published window the day before the new one starts.
        var prior = overlap.Where(v => v.EffectiveFrom < effectiveFrom).ToList();
        var conflicting = overlap.FirstOrDefault(v => v.EffectiveFrom >= effectiveFrom);
        if (conflicting is not null)
            return Error.Conflict("shift_version.overlap",
                $"A published version already covers {conflicting.EffectiveFrom:yyyy-MM-dd} onward — retire it or choose a later start.");
        // Close-then-insert in separate saves inside one transaction: the gist exclusion constraint is
        // checked per-statement, and a failed insert must not leave the prior window truncated. The
        // execution-strategy wrapper is required by EnableRetryOnFailure.
        var published = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // The strategy may re-run this delegate; reload so each attempt starts from committed state,
            // then re-verify the draft check — a concurrent publish may have committed in between.
            await ((DbContext)db).Entry(version).ReloadAsync(ct);
            if (version.Status != AttendanceVersionStatuses.Draft) return false;
            foreach (var p in prior) await ((DbContext)db).Entry(p).ReloadAsync(ct);
            foreach (var p in prior) p.EffectiveTo = effectiveFrom.AddDays(-1);
            if (prior.Count > 0) await db.SaveChangesAsync(ct);
            version.Status = AttendanceVersionStatuses.Published;
            version.EffectiveFrom = effectiveFrom;
            version.EffectiveTo = null;
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false; // lost a concurrent status transition — report as conflict below
            }
            return true;
        });
        if (!published)
            return Error.Conflict("shift_version.not_draft", "The version was published or retired concurrently.");
        return version.ToDto();
    }

    public async Task<Result> RetireShiftVersionAsync(Guid versionId, DateOnly effectiveTo, bool canBackdate, CancellationToken ct)
    {
        var version = await db.ShiftVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("shift_version.not_found", "Shift version not found.");
        if (version.Status != AttendanceVersionStatuses.Published)
            return Error.Conflict("shift_version.not_published", "Only published versions can be retired.");
        if (effectiveTo < version.EffectiveFrom)
            return Error.Validation("shift_version.bad_window", "Effective-to cannot precede effective-from.");
        if (effectiveTo < Today && !canBackdate)
            return Error.Forbidden("shift_version.backdate_forbidden", "Backdated retirement requires the attendance.masters.backdate permission.");
        version.Status = AttendanceVersionStatuses.Retired;
        version.EffectiveTo = effectiveTo;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Error? ValidateShiftVersion(ShiftVersionInput input)
    {
        if (!ShiftTypes.All.Contains(input.ShiftType))
            return Error.Validation("shift_version.type", $"shift_type must be one of {string.Join(", ", ShiftTypes.All)}.");
        if (input.BreakMinutes is < 0 or > 480)
            return Error.Validation("shift_version.break_minutes", "break_minutes must be 0-480.");
        if (input.ShiftType != ShiftTypes.Fixed && input.RequiredMinutes is null or <= 0)
            return Error.Validation("shift_version.required_minutes", "flexi/open shifts require required_minutes > 0.");
        if (!string.IsNullOrEmpty(input.WeeklyOffConfigJson))
        {
            var err = Organization.WeeklyOffRules.Validate(Organization.WeeklyOffPatterns.ShiftBased, input.WeeklyOffConfigJson);
            if (err is not null) return Error.Validation("shift_version.weekly_off_config", err);
        }
        return null;
    }

    private static void ApplyShiftVersionInput(ShiftVersion v, ShiftVersionInput input)
    {
        v.ShiftType = input.ShiftType;
        v.StartTime = input.StartTime;
        v.EndTime = input.EndTime;
        v.BreakMinutes = input.BreakMinutes;
        v.BreakPaid = input.BreakPaid;
        v.CoreStart = input.CoreStart;
        v.CoreEnd = input.CoreEnd;
        v.RequiredMinutes = input.RequiredMinutes;
        v.GraceInMinutes = input.GraceInMinutes;
        v.GraceOutMinutes = input.GraceOutMinutes;
        v.InWindowBeforeMinutes = input.InWindowBeforeMinutes ?? 120;
        v.OutWindowAfterMinutes = input.OutWindowAfterMinutes ?? 240;
        v.OtAllowed = input.OtAllowed;
        v.WeeklyOffConfigJson = input.WeeklyOffConfigJson;
    }

    // ---------- Calendars ----------

    public async Task<IReadOnlyList<WorkCalendarDto>> ListCalendarsAsync(CancellationToken ct)
    {
        var calendars = await db.WorkCalendars.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
        var counts = await db.CalendarDays.GroupBy(d => d.CalendarId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return calendars.Select(c => c.ToDto(counts.FirstOrDefault(x => x.Key == c.Id)?.Count ?? 0)).ToList();
    }

    public async Task<Result<WorkCalendarDto>> CreateCalendarAsync(WorkCalendarInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 64)
            return Error.Validation("calendar.code", "Code is required (max 64 chars).");
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.WorkCalendars.AnyAsync(c => c.Code == code, ct))
            return Error.Conflict("calendar.duplicate_code", $"Calendar '{code}' already exists.");
        if (input.IsDefault && await db.WorkCalendars.AnyAsync(c => c.IsDefault && c.IsActive, ct))
            return Error.Conflict("calendar.default_exists", "A default calendar already exists — unset it first.");
        var cal = new WorkCalendar { Code = code, Name = input.Name.Trim(), Year = input.Year, IsDefault = input.IsDefault, IsActive = input.IsActive };
        db.WorkCalendars.Add(cal);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TryMapUniqueViolation(ex, out var conflict))
        {
            return conflict!;
        }
        return cal.ToDto(0);
    }

    public async Task<Result> UpdateCalendarAsync(Guid id, WorkCalendarInput input, CancellationToken ct)
    {
        var cal = await db.WorkCalendars.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cal is null) return Error.NotFound("calendar.not_found", "Calendar not found.");
        if (!string.Equals(cal.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase)
            && await db.WorkCalendars.AnyAsync(c => c.Code == input.Code.Trim(), ct))
            return Error.Conflict("calendar.duplicate_code", $"Calendar '{input.Code}' already exists.");
        if (input.IsDefault && !cal.IsDefault && await db.WorkCalendars.AnyAsync(c => c.IsDefault && c.IsActive && c.Id != id, ct))
            return Error.Conflict("calendar.default_exists", "A default calendar already exists — unset it first.");
        cal.Code = input.Code.Trim().ToUpperInvariant();
        cal.Name = input.Name.Trim();
        cal.Year = input.Year;
        cal.IsDefault = input.IsDefault;
        cal.IsActive = input.IsActive;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TryMapUniqueViolation(ex, out var conflict))
        {
            return conflict!;
        }
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<CalendarDayDto>>> ListCalendarDaysAsync(
        Guid calendarId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!await db.WorkCalendars.AnyAsync(c => c.Id == calendarId, ct))
            return Error.NotFound("calendar.not_found", "Calendar not found.");
        var q = db.CalendarDays.AsNoTracking().Where(d => d.CalendarId == calendarId);
        if (from is { } f) q = q.Where(d => d.Date >= f);
        if (to is { } t) q = q.Where(d => d.Date <= t);
        return Result<IReadOnlyList<CalendarDayDto>>.Ok(
            (await q.OrderBy(d => d.Date).ToListAsync(ct)).Select(d => d.ToDto()).ToList());
    }

    /// <summary>Bulk-upsert calendar days (used by the editor and CSV import).</summary>
    public async Task<Result<int>> UpsertCalendarDaysAsync(Guid calendarId, IReadOnlyList<CalendarDayInput> days, CancellationToken ct)
    {
        if (!await db.WorkCalendars.AnyAsync(c => c.Id == calendarId, ct))
            return Error.NotFound("calendar.not_found", "Calendar not found.");
        var dates = days.Select(d => d.Date).ToList();
        var existing = await db.CalendarDays.Where(d => d.CalendarId == calendarId && dates.Contains(d.Date)).ToListAsync(ct);
        var upserted = 0;
        foreach (var input in days)
        {
            if (!CalendarDayTypes.All.Contains(input.DayType))
                return Error.Validation("calendar_day.type", $"day_type must be one of {string.Join(", ", CalendarDayTypes.All)}.");
            var row = existing.FirstOrDefault(d => d.Date == input.Date);
            if (row is null)
            {
                row = new CalendarDay { CalendarId = calendarId, Date = input.Date };
                db.CalendarDays.Add(row);
            }
            row.DayType = input.DayType;
            row.Name = input.Name?.Trim() ?? input.DayType;
            row.HalfDayMinutes = input.HalfDayMinutes;
            row.AppliesToJson = input.AppliesToJson;
            upserted++;
        }
        await db.SaveChangesAsync(ct);
        return upserted;
    }

    public async Task<Result> DeleteCalendarDayAsync(Guid calendarId, DateOnly date, CancellationToken ct)
    {
        var row = await db.CalendarDays.FirstOrDefaultAsync(d => d.CalendarId == calendarId && d.Date == date, ct);
        if (row is null) return Error.NotFound("calendar_day.not_found", "Calendar day not found.");
        db.CalendarDays.Remove(row);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Policies ----------

    public async Task<IReadOnlyList<AttendancePolicyDto>> ListPoliciesAsync(CancellationToken ct)
    {
        var today = Today;
        var policies = await db.AttendancePolicies.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);
        var versions = await db.PolicyVersions.AsNoTracking()
            .Where(v => v.Status == AttendanceVersionStatuses.Published).ToListAsync(ct);
        var counts = await db.PolicyVersions.GroupBy(v => v.PolicyId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return policies.Select(p =>
        {
            var active = versions.Where(v => v.PolicyId == p.Id && v.EffectiveFrom <= today && (v.EffectiveTo == null || v.EffectiveTo >= today))
                .OrderByDescending(v => v.EffectiveFrom).FirstOrDefault();
            return p.ToDto(counts.FirstOrDefault(c => c.Key == p.Id)?.Count ?? 0, active);
        }).ToList();
    }

    public async Task<Result<AttendancePolicyDto>> CreatePolicyAsync(AttendancePolicyInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 64)
            return Error.Validation("policy.code", "Code is required (max 64 chars).");
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.AttendancePolicies.AnyAsync(p => p.Code == code, ct))
            return Error.Conflict("policy.duplicate_code", $"Policy '{code}' already exists.");
        if (input.IsDefault && await db.AttendancePolicies.AnyAsync(p => p.IsDefault && p.IsActive, ct))
            return Error.Conflict("policy.default_exists", "A default policy already exists — unset it first.");
        var policy = new AttendancePolicy { Code = code, Name = input.Name.Trim(), IsDefault = input.IsDefault, IsActive = input.IsActive };
        db.AttendancePolicies.Add(policy);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TryMapUniqueViolation(ex, out var conflict))
        {
            return conflict!;
        }
        return policy.ToDto(0, null);
    }

    public async Task<Result> UpdatePolicyAsync(Guid id, AttendancePolicyInput input, CancellationToken ct)
    {
        var policy = await db.AttendancePolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return Error.NotFound("policy.not_found", "Policy not found.");
        if (!string.Equals(policy.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase)
            && await db.AttendancePolicies.AnyAsync(p => p.Code == input.Code.Trim(), ct))
            return Error.Conflict("policy.duplicate_code", $"Policy '{input.Code}' already exists.");
        if (input.IsDefault && !policy.IsDefault && await db.AttendancePolicies.AnyAsync(p => p.IsDefault && p.IsActive && p.Id != id, ct))
            return Error.Conflict("policy.default_exists", "A default policy already exists — unset it first.");
        policy.Code = input.Code.Trim().ToUpperInvariant();
        policy.Name = input.Name.Trim();
        policy.IsDefault = input.IsDefault;
        policy.IsActive = input.IsActive;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TryMapUniqueViolation(ex, out var conflict))
        {
            return conflict!;
        }
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PolicyVersionDto>>> ListPolicyVersionsAsync(Guid policyId, CancellationToken ct)
    {
        if (!await db.AttendancePolicies.AnyAsync(p => p.Id == policyId, ct))
            return Error.NotFound("policy.not_found", "Policy not found.");
        return Result<IReadOnlyList<PolicyVersionDto>>.Ok(
            (await db.PolicyVersions.AsNoTracking().Where(v => v.PolicyId == policyId)
                .OrderByDescending(v => v.Version).ToListAsync(ct)).Select(v => v.ToDto()).ToList());
    }

    public async Task<Result<PolicyVersionDto>> CreatePolicyVersionAsync(Guid policyId, PolicyVersionInput input, CancellationToken ct)
    {
        if (!await db.AttendancePolicies.AnyAsync(p => p.Id == policyId, ct))
            return Error.NotFound("policy.not_found", "Policy not found.");
        var errors = AttendanceRulesV1.Validate(input.Rules);
        if (errors.Count > 0)
            return Error.Validation("rules.invalid", "Attendance rules failed validation.", errors);
        var next = await db.PolicyVersions.Where(v => v.PolicyId == policyId).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = new PolicyVersion
        {
            PolicyId = policyId, Version = next + 1,
            EffectiveFrom = Today.AddDays(1),
            RulesJson = input.Rules.ToJson(),
        };
        db.PolicyVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return version.ToDto();
    }

    public async Task<Result> UpdatePolicyVersionAsync(Guid versionId, PolicyVersionInput input, CancellationToken ct)
    {
        var version = await db.PolicyVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("policy_version.not_found", "Policy version not found.");
        if (version.Status != AttendanceVersionStatuses.Draft)
            return Error.Conflict("policy_version.immutable", $"A {version.Status} version is immutable — create a new draft.");
        var errors = AttendanceRulesV1.Validate(input.Rules);
        if (errors.Count > 0)
            return Error.Validation("rules.invalid", "Attendance rules failed validation.", errors);
        version.RulesJson = input.Rules.ToJson();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("policy_version.immutable", "The version was published or retired concurrently — create a new draft.");
        }
        return Result.Success();
    }

    public async Task<Result<PolicyVersionDto>> PublishPolicyVersionAsync(
        Guid versionId, DateOnly effectiveFrom, bool canBackdate, CancellationToken ct)
    {
        var version = await db.PolicyVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("policy_version.not_found", "Policy version not found.");
        if (version.Status != AttendanceVersionStatuses.Draft)
            return Error.Conflict("policy_version.not_draft", $"Only draft versions can be published (current: {version.Status}).");
        if (effectiveFrom < Today && !canBackdate)
            return Error.Forbidden("policy_version.backdate_forbidden", "Backdated publication requires the attendance.masters.backdate permission.");
        var overlap = await db.PolicyVersions
            .Where(v => v.PolicyId == version.PolicyId && v.Status == AttendanceVersionStatuses.Published
                && (v.EffectiveTo == null || v.EffectiveTo >= effectiveFrom))
            .OrderBy(v => v.EffectiveFrom).ToListAsync(ct);
        var priors = overlap.Where(v => v.EffectiveFrom < effectiveFrom).ToList();
        var conflicting = overlap.FirstOrDefault(v => v.EffectiveFrom >= effectiveFrom);
        if (conflicting is not null)
            return Error.Conflict("policy_version.overlap",
                $"A published version already covers {conflicting.EffectiveFrom:yyyy-MM-dd} onward — retire it or choose a later start.");
        var published = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ((DbContext)db).Entry(version).ReloadAsync(ct);
            if (version.Status != AttendanceVersionStatuses.Draft) return false;
            foreach (var p in priors) await ((DbContext)db).Entry(p).ReloadAsync(ct);
            foreach (var p in priors) p.EffectiveTo = effectiveFrom.AddDays(-1);
            if (priors.Count > 0) await db.SaveChangesAsync(ct);
            version.Status = AttendanceVersionStatuses.Published;
            version.EffectiveFrom = effectiveFrom;
            version.EffectiveTo = null;
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false; // lost a concurrent status transition — report as conflict below
            }
            return true;
        });
        if (!published)
            return Error.Conflict("policy_version.not_draft", "The version was published or retired concurrently.");
        return version.ToDto();
    }

    public async Task<Result> RetirePolicyVersionAsync(Guid versionId, DateOnly effectiveTo, bool canBackdate, CancellationToken ct)
    {
        var version = await db.PolicyVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return Error.NotFound("policy_version.not_found", "Policy version not found.");
        if (version.Status != AttendanceVersionStatuses.Published)
            return Error.Conflict("policy_version.not_published", "Only published versions can be retired.");
        if (effectiveTo < version.EffectiveFrom)
            return Error.Validation("policy_version.bad_window", "Effective-to cannot precede effective-from.");
        if (effectiveTo < Today && !canBackdate)
            return Error.Forbidden("policy_version.backdate_forbidden", "Backdated retirement requires the attendance.masters.backdate permission.");
        version.Status = AttendanceVersionStatuses.Retired;
        version.EffectiveTo = effectiveTo;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Assignments ----------

    public async Task<Result<IReadOnlyList<ShiftAssignmentDto>>> ListShiftAssignmentsAsync(
        Guid? employeeId, bool onlyCurrent, CancellationToken ct)
    {
        var today = Today;
        var q = db.ShiftAssignments.AsNoTracking().AsQueryable();
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.AssignmentsManage, ct);
        q = scopes.ApplyEmployeeScope(q, a => a.EmployeeId, scope);
        if (employeeId is { } emp) q = q.Where(a => a.EmployeeId == emp);
        if (onlyCurrent) q = q.Where(a => a.EffectiveFrom <= today && (a.EffectiveTo == null || a.EffectiveTo >= today));
        var rows = await q.OrderByDescending(a => a.EffectiveFrom).ToListAsync(ct);
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var shiftIds = rows.Select(r => r.ShiftId).Distinct().ToList();
        var emps = await db.Employees.Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeCode, e.DisplayName }).ToDictionaryAsync(e => e.Id, ct);
        var shifts = await db.Shifts.Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return rows.Where(r => emps.ContainsKey(r.EmployeeId) && shifts.ContainsKey(r.ShiftId))
            .Select(r => new ShiftAssignmentDto(r.Id, r.EmployeeId, emps[r.EmployeeId].EmployeeCode,
                emps[r.EmployeeId].DisplayName, r.ShiftId, shifts[r.ShiftId].Code, shifts[r.ShiftId].Name,
                r.EffectiveFrom, r.EffectiveTo, r.Reason, r.Source)).ToList();
    }

    public async Task<Result<Guid>> CreateShiftAssignmentAsync(ShiftAssignmentInput input, CancellationToken ct)
    {
        if (!await EmployeeInScopeAsync(input.EmployeeId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        if (!await db.Shifts.AnyAsync(s => s.Id == input.ShiftId && s.IsActive, ct))
            return Error.NotFound("shift.not_found", "Shift not found or inactive.");
        if (input.EffectiveTo is { } to && to < input.EffectiveFrom)
            return Error.Validation("assignment.window", "effective_to cannot precede effective_from.");
        // Close any overlapping open assignment; a same-start overlap is rejected.
        var overlapping = await db.ShiftAssignments
            .Where(a => a.EmployeeId == input.EmployeeId && (a.EffectiveTo == null || a.EffectiveTo >= input.EffectiveFrom))
            .ToListAsync(ct);
        if (overlapping.Any(a => a.EffectiveFrom >= input.EffectiveFrom))
            return Error.Conflict("assignment.overlap", "An assignment already covers that start date — end it first.");
        var createdId = Guid.Empty;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var prior in overlapping)
            {
                await ((DbContext)db).Entry(prior).ReloadAsync(ct);
                prior.EffectiveTo = input.EffectiveFrom.AddDays(-1);
            }
            if (overlapping.Count > 0) await db.SaveChangesAsync(ct);
            var assignment = new ShiftAssignment
            {
                EmployeeId = input.EmployeeId, ShiftId = input.ShiftId,
                EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo, Reason = input.Reason,
            };
            db.ShiftAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            createdId = assignment.Id;
        });
        return createdId;
    }

    public async Task<Result> EndShiftAssignmentAsync(Guid id, DateOnly effectiveTo, CancellationToken ct)
    {
        var a = await db.ShiftAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("assignment.not_found", "Assignment not found.");
        if (!await EmployeeInScopeAsync(a.EmployeeId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        if (effectiveTo < a.EffectiveFrom)
            return Error.Validation("assignment.window", "effective_to cannot precede effective_from.");
        // Ending past another assignment's start would overlap it — reject, don't rely on the DB constraint.
        var collides = await db.ShiftAssignments.AnyAsync(x => x.Id != id && x.EmployeeId == a.EmployeeId
            && x.EffectiveFrom <= effectiveTo && (x.EffectiveTo == null || x.EffectiveTo >= a.EffectiveFrom), ct);
        if (collides)
            return Error.Conflict("assignment.overlap", "Effective end overlaps another assignment for this employee.");
        a.EffectiveTo = effectiveTo;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PolicyAssignmentDto>>> ListPolicyAssignmentsAsync(Guid? employeeId, CancellationToken ct)
    {
        var q = db.PolicyAssignments.AsNoTracking().AsQueryable();
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.AssignmentsManage, ct);
        q = scopes.ApplyEmployeeScope(q, a => a.EmployeeId, scope);
        if (employeeId is { } emp) q = q.Where(a => a.EmployeeId == emp);
        var rows = await q.OrderByDescending(a => a.EffectiveFrom).ToListAsync(ct);
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var policyIds = rows.Select(r => r.PolicyId).Distinct().ToList();
        var emps = await db.Employees.Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeCode, e.DisplayName }).ToDictionaryAsync(e => e.Id, ct);
        var policies = await db.AttendancePolicies.Where(p => policyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return rows.Where(r => emps.ContainsKey(r.EmployeeId) && policies.ContainsKey(r.PolicyId))
            .Select(r => new PolicyAssignmentDto(r.Id, r.EmployeeId, emps[r.EmployeeId].EmployeeCode,
                emps[r.EmployeeId].DisplayName, r.PolicyId, policies[r.PolicyId].Code, policies[r.PolicyId].Name,
                r.EffectiveFrom, r.EffectiveTo, r.Reason)).ToList();
    }

    public async Task<Result<Guid>> CreatePolicyAssignmentAsync(PolicyAssignmentInput input, CancellationToken ct)
    {
        if (!await EmployeeInScopeAsync(input.EmployeeId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        if (!await db.AttendancePolicies.AnyAsync(p => p.Id == input.PolicyId && p.IsActive, ct))
            return Error.NotFound("policy.not_found", "Policy not found or inactive.");
        if (input.EffectiveTo is { } to && to < input.EffectiveFrom)
            return Error.Validation("assignment.window", "effective_to cannot precede effective_from.");
        var overlapping = await db.PolicyAssignments
            .Where(a => a.EmployeeId == input.EmployeeId && (a.EffectiveTo == null || a.EffectiveTo >= input.EffectiveFrom))
            .ToListAsync(ct);
        if (overlapping.Any(a => a.EffectiveFrom >= input.EffectiveFrom))
            return Error.Conflict("assignment.overlap", "An assignment already covers that start date — end it first.");
        var createdId = Guid.Empty;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var prior in overlapping)
            {
                await ((DbContext)db).Entry(prior).ReloadAsync(ct);
                prior.EffectiveTo = input.EffectiveFrom.AddDays(-1);
            }
            if (overlapping.Count > 0) await db.SaveChangesAsync(ct);
            var assignment = new PolicyAssignment
            {
                EmployeeId = input.EmployeeId, PolicyId = input.PolicyId,
                EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo, Reason = input.Reason,
            };
            db.PolicyAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            createdId = assignment.Id;
        });
        return createdId;
    }

    public async Task<Result> EndPolicyAssignmentAsync(Guid id, DateOnly effectiveTo, CancellationToken ct)
    {
        var a = await db.PolicyAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("assignment.not_found", "Assignment not found.");
        if (!await EmployeeInScopeAsync(a.EmployeeId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        if (effectiveTo < a.EffectiveFrom)
            return Error.Validation("assignment.window", "effective_to cannot precede effective_from.");
        var collides = await db.PolicyAssignments.AnyAsync(x => x.Id != id && x.EmployeeId == a.EmployeeId
            && x.EffectiveFrom <= effectiveTo && (x.EffectiveTo == null || x.EffectiveTo >= a.EffectiveFrom), ct);
        if (collides)
            return Error.Conflict("assignment.overlap", "Effective end overlaps another assignment for this employee.");
        a.EffectiveTo = effectiveTo;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Rosters ----------

    public async Task<Result<IReadOnlyList<RosterEntry>>> ListRosterAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!await EmployeeInScopeAsync(employeeId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        var rows = await db.AttendanceRosters.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.WorkDate >= from && r.WorkDate <= to)
            .OrderBy(r => r.WorkDate).ToListAsync(ct);
        return rows.Select(r => new RosterEntry(r.EmployeeId, r.WorkDate, r.ShiftId, r.IsOff, r.Reason, r.Source)).ToList();
    }

    /// <summary>Bulk-upsert roster rows (one per employee+work_date).</summary>
    public async Task<Result<RosterBulkResult>> UpsertRosterAsync(RosterUpsertInput input, CancellationToken ct)
    {
        var empIds = input.Entries.Select(e => e.EmployeeId).Distinct().ToList();
        var dates = input.Entries.Select(e => e.WorkDate).Distinct().ToList();
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.AssignmentsManage, ct);
        var validEmps = (await scopes.ApplyEmployeeScope(
                db.Employees.Where(e => empIds.Contains(e.Id)), e => e.Id, scope)
            .Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        var shiftIds = input.Entries.Where(e => e.ShiftId != null).Select(e => e.ShiftId!.Value).Distinct().ToList();
        var validShifts = (await db.Shifts.Where(s => shiftIds.Contains(s.Id) && s.IsActive).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        var existing = await db.AttendanceRosters
            .Where(r => empIds.Contains(r.EmployeeId) && dates.Contains(r.WorkDate)).ToListAsync(ct);
        var upserted = 0;
        var skipped = 0;
        foreach (var entry in input.Entries)
        {
            if (!validEmps.Contains(entry.EmployeeId)
                || (entry.ShiftId is { } sid && !validShifts.Contains(sid))
                || (!entry.IsOff && entry.ShiftId is null))
            {
                skipped++;
                continue;
            }
            var row = existing.FirstOrDefault(r => r.EmployeeId == entry.EmployeeId && r.WorkDate == entry.WorkDate);
            if (row is null)
            {
                row = new AttendanceRoster { EmployeeId = entry.EmployeeId, WorkDate = entry.WorkDate };
                db.AttendanceRosters.Add(row);
            }
            row.ShiftId = entry.IsOff ? null : entry.ShiftId;
            row.IsOff = entry.IsOff;
            row.Reason = entry.Reason;
            row.Source = entry.Source;
            upserted++;
        }
        await db.SaveChangesAsync(ct);
        return new RosterBulkResult(upserted, skipped);
    }

    private async Task<bool> EmployeeInScopeAsync(Guid employeeId, CancellationToken ct)
    {
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.AssignmentsManage, ct);
        return await scopes.ApplyEmployeeScope(db.Employees.Where(e => e.Id == employeeId), e => e.Id, scope).AnyAsync(ct);
    }

    // Postgres raises 23505 when the pre-check loses a concurrent race (e.g. two requests
    // inserting is_default=true rows past the AnyAsync guard). Application has no Npgsql
    // reference, so the constraint is identified from the provider message text.
    private static bool TryMapUniqueViolation(DbUpdateException ex, out Error? conflict)
    {
        var msg = ex.InnerException?.Message;
        conflict = msg is not null && msg.Contains("23505", StringComparison.Ordinal)
            ? msg switch
            {
                var m when m.Contains("ix_attendance_policies_is_default", StringComparison.Ordinal)
                    => Error.Conflict("policy.default_exists", "A default policy already exists — unset it first."),
                var m when m.Contains("ix_work_calendars_is_default", StringComparison.Ordinal)
                    => Error.Conflict("calendar.default_exists", "A default calendar already exists — unset it first."),
                var m when m.Contains("ix_attendance_policies_code", StringComparison.Ordinal)
                    => Error.Conflict("policy.duplicate_code", "A policy with this code already exists."),
                var m when m.Contains("ix_work_calendars_code", StringComparison.Ordinal)
                    => Error.Conflict("calendar.duplicate_code", "A calendar with this code already exists."),
                _ => null,
            }
            : null;
        return conflict is not null;
    }
}
