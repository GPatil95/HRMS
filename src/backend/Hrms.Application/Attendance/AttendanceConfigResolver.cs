using Hrms.Application.Abstractions;
using Hrms.Application.Organization;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Attendance;

public static class ResolvedDayTypes
{
    public const string Working = "working";
    public const string WeeklyOff = "weekly_off";
    public const string Holiday = "holiday";
    public const string OptionalHoliday = "optional_holiday";
    public const string WorkingOverride = "working_override";
    public const string HalfDay = "half_day";
    public const string RosteredOff = "roster_off";
}

/// <summary>
/// M3.1 effective-configuration resolver: for an employee + local work-date returns the
/// day-type, resolved shift/policy/calendar (with which layer supplied each) and the planned
/// window as absolute instants. Versioned masters mean resolution is point-in-time stable:
/// editing a draft never changes what an old date resolved to.
/// </summary>
public sealed class AttendanceConfigResolver(ITenantDbContext db)
{
    /// <summary>Resolve a single employee+date.</summary>
    public async Task<Result<ResolvedAttendanceDto>> ResolveAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Error.NotFound("employee.not_found", "Employee not found.");
        return await ResolveCoreAsync(employee, date, ct);
    }

    /// <summary>Resolve a range, loading master data once.</summary>
    public async Task<Result<IReadOnlyList<ResolvedAttendanceDto>>> ResolveRangeAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Error.NotFound("employee.not_found", "Employee not found.");
        if (to < from || (to.DayNumber - from.DayNumber) > 92)
            return Error.Validation("resolve.range", "Range must be from..to within 92 days.");
        var result = new List<ResolvedAttendanceDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = await ResolveCoreAsync(employee, d, ct);
            if (!day.IsSuccess) return day.Error!;
            result.Add(day.Value);
        }
        return result;
    }

    private async Task<Result<ResolvedAttendanceDto>> ResolveCoreAsync(Employee employee, DateOnly date, CancellationToken ct)
    {
        // 1. Org placement as-of date (EmploymentHistory wins over denormalized employee fields).
        var org = await db.EmploymentHistory.AsNoTracking()
            .Where(h => h.EmployeeId == employee.Id && h.EffectiveFrom <= date && (h.EffectiveTo == null || h.EffectiveTo >= date))
            .OrderByDescending(h => h.EffectiveFrom).FirstOrDefaultAsync(ct);
        var companyId = org?.CompanyId ?? employee.CompanyId;
        var branchId = org?.BranchId ?? employee.BranchId;
        // When a history row exists it is authoritative: a null DepartmentId means departmentless then.
        var departmentId = org is null ? employee.DepartmentId : org.DepartmentId;
        var orgSource = org is null ? "employee-current" : "employment-history";

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        // branch/dept/company TimeZone fields are IANA ids
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct);
        var department = departmentId is { } deptId
            ? await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deptId, ct) : null;
        var timeZoneId = branch?.TimeZone ?? "Asia/Kolkata";
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; timeZoneId = "UTC"; }

        // 2. Shift: roster row → effective assignment → org default chain.
        var roster = await db.AttendanceRosters.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.WorkDate == date, ct);
        Shift? shift = null;
        string? shiftSource = null;
        var rosteredOff = roster?.IsOff == true;
        if (roster?.ShiftId is { } rosterShiftId)
        {
            shift = await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == rosterShiftId && s.IsActive, ct);
            shiftSource = "roster";
        }
        if (shift is null && !rosteredOff)
        {
            var assignment = await db.ShiftAssignments.AsNoTracking()
                .Where(a => a.EmployeeId == employee.Id && a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date))
                .OrderByDescending(a => a.EffectiveFrom).FirstOrDefaultAsync(ct);
            if (assignment is not null)
            {
                shift = await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == assignment.ShiftId && s.IsActive, ct);
                if (shift is not null) shiftSource = "assignment";
            }
        }
        if (shift is null && !rosteredOff)
        {
            var defaultCode = FirstNonEmpty(department?.DefaultShiftName, branch?.DefaultShiftName, company?.DefaultShiftName);
            if (defaultCode is not null)
            {
                shift = await db.Shifts.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.IsActive && (s.Code == defaultCode || s.Name == defaultCode), ct);
                if (shift is not null) shiftSource = "org-default";
            }
        }

        // Retired versions keep their window for historical dates; drafts never resolve.
        var shiftVersion = shift is null ? null : await db.ShiftVersions.AsNoTracking()
            .Where(v => v.ShiftId == shift.Id && v.Status != AttendanceVersionStatuses.Draft
                && v.EffectiveFrom <= date && (v.EffectiveTo == null || v.EffectiveTo >= date))
            .OrderByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct);

        // 3. Policy: explicit assignment → dept → branch → company → default.
        AttendancePolicy? policy = null;
        string? policySource = null;
        var policyAssignment = await db.PolicyAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date))
            .OrderByDescending(a => a.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (policyAssignment is not null)
        {
            policy = await db.AttendancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == policyAssignment.PolicyId && p.IsActive, ct);
            if (policy is not null) policySource = "assignment";
        }
        if (policy is null)
        {
            var code = FirstNonEmpty(department?.AttendancePolicyCode, branch?.AttendancePolicyCode, company?.DefaultAttendancePolicyCode);
            if (code is not null)
            {
                policy = await db.AttendancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);
                if (policy is not null) policySource = "org-code";
            }
        }
        if (policy is null)
        {
            policy = await db.AttendancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault && p.IsActive, ct);
            if (policy is not null) policySource = "tenant-default";
        }

        var policyVersion = policy is null ? null : await db.PolicyVersions.AsNoTracking()
            .Where(v => v.PolicyId == policy.Id && v.Status != AttendanceVersionStatuses.Draft
                && v.EffectiveFrom <= date && (v.EffectiveTo == null || v.EffectiveTo >= date))
            .OrderByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct);
        var rules = policyVersion is null ? AttendanceRulesV1.Defaults()
            : AttendanceRulesV1.Parse(policyVersion.RulesJson) ?? AttendanceRulesV1.Defaults();

        // 4. Calendar: dept → branch → company → default.
        var calendarCode = FirstNonEmpty(department?.HolidayCalendarCode, branch?.HolidayCalendarCode, company?.DefaultHolidayCalendarCode);
        WorkCalendar? calendar = null;
        string? calendarSource = null;
        if (calendarCode is not null)
        {
            calendar = await db.WorkCalendars.AsNoTracking().FirstOrDefaultAsync(c => c.Code == calendarCode && c.IsActive, ct);
            if (calendar is not null) calendarSource = "org-code";
        }
        if (calendar is null)
        {
            calendar = await db.WorkCalendars.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && c.IsActive, ct);
            if (calendar is not null) calendarSource = "tenant-default";
        }
        var calendarDay = calendar is null ? null : await db.CalendarDays.AsNoTracking()
            .FirstOrDefaultAsync(d => d.CalendarId == calendar.Id && d.Date == date, ct);
        // Per-day scoping: AppliesToJson {"branch_ids":[],"department_ids":[]} narrows applicability.
        var scopedDay = calendarDay is not null && DayApplies(calendarDay, branchId, departmentId) ? calendarDay : null;

        // 5. Day-type precedence: roster > working_override > holiday > weekly-off > working.
        string dayType;
        if (rosteredOff)
        {
            dayType = ResolvedDayTypes.RosteredOff;
        }
        else if (scopedDay?.DayType == CalendarDayTypes.WorkingOverride)
        {
            dayType = ResolvedDayTypes.WorkingOverride;
        }
        else if (scopedDay is not null)
        {
            dayType = scopedDay.DayType == CalendarDayTypes.HalfDay ? ResolvedDayTypes.HalfDay
                : scopedDay.DayType == CalendarDayTypes.OptionalHoliday ? ResolvedDayTypes.OptionalHoliday
                : ResolvedDayTypes.Holiday;
        }
        else
        {
            // Weekly off: org chain pattern; 'shift_based' evaluates every rule in the shift version's config.
            var (pattern, cfgJson) = ResolveWeeklyOff(department, branch, company);
            var isOff = pattern == WeeklyOffPatterns.ShiftBased
                ? WeeklyOffRules.IsOffByConfig(shiftVersion?.WeeklyOffConfigJson is { } j ? WeeklyOffRules.Parse(j) : null, date)
                : WeeklyOffRules.IsWeeklyOff(pattern, WeeklyOffRules.Parse(cfgJson), date);
            dayType = isOff ? ResolvedDayTypes.WeeklyOff : ResolvedDayTypes.Working;
        }
        // A rostered working day is an explicit schedule: it overrides a natural off (holiday/weekly-off).
        if (roster?.ShiftId is not null
            && dayType is ResolvedDayTypes.Holiday or ResolvedDayTypes.OptionalHoliday or ResolvedDayTypes.WeeklyOff)
        {
            dayType = ResolvedDayTypes.WorkingOverride;
        }

        // 6. Planned instants in branch tz (overnight shifts cross midnight; DST handled by TZ rules).
        DateTimeOffset? plannedStart = null;
        DateTimeOffset? plannedEnd = null;
        int? plannedMinutes = null;
        var plannedDay = dayType is ResolvedDayTypes.Working or ResolvedDayTypes.WorkingOverride or ResolvedDayTypes.HalfDay;
        if (shiftVersion is not null && plannedDay)
        {
            var startLocal = date.ToDateTime(shiftVersion.StartTime);
            var endLocal = date.ToDateTime(shiftVersion.EndTime);
            if (shiftVersion.EndTime <= shiftVersion.StartTime) endLocal = endLocal.AddDays(1);
            var startUtc = SafeToUtc(tz, startLocal);
            var endUtc = SafeToUtc(tz, endLocal);
            plannedStart = new DateTimeOffset(startUtc, TimeSpan.Zero);
            plannedEnd = new DateTimeOffset(endUtc, TimeSpan.Zero);
            var span = (int)(endUtc - startUtc).TotalMinutes - (shiftVersion.BreakPaid ? 0 : shiftVersion.BreakMinutes);
            plannedMinutes = scopedDay?.DayType == CalendarDayTypes.HalfDay && scopedDay.HalfDayMinutes is { } hm
                ? hm : shiftVersion.RequiredMinutes ?? span;
        }

        // Grace: shift version → policy → org chain → 0.
        var orgGrace = department?.GraceMinutes ?? branch?.GraceMinutes ?? company?.GraceMinutes ?? 0;
        var graceIn = shiftVersion?.GraceInMinutes ?? (rules.Late.GraceMinutes > 0 ? rules.Late.GraceMinutes : orgGrace);
        var graceOut = shiftVersion?.GraceOutMinutes ?? (rules.Early.GraceMinutes > 0 ? rules.Early.GraceMinutes : orgGrace);

        return new ResolvedAttendanceDto(
            employee.Id, date, timeZoneId, dayType,
            shift?.Id, shift?.Code, shiftVersion?.Version, shiftSource,
            policy?.Id, policy?.Code, policyVersion?.Version, policySource,
            calendar?.Id, calendar?.Code, calendarSource,
            plannedStart, plannedEnd, plannedMinutes,
            graceIn, graceOut, orgSource);
    }

    private static (string? pattern, string? cfgJson) ResolveWeeklyOff(Department? dept, Branch? branch, Company? company)
    {
        if (!string.IsNullOrEmpty(dept?.WeeklyOffPattern)) return (dept.WeeklyOffPattern, dept.WeeklyOffConfigJson);
        if (!string.IsNullOrEmpty(branch?.WeeklyOffPattern)) return (branch.WeeklyOffPattern, branch.WeeklyOffConfigJson);
        if (!string.IsNullOrEmpty(company?.WeeklyOffPattern)) return (company.WeeklyOffPattern, company.WeeklyOffConfigJson);
        return (null, null);
    }

    private static bool DayApplies(CalendarDay day, Guid branchId, Guid? departmentId)
    {
        if (string.IsNullOrWhiteSpace(day.AppliesToJson)) return true;
        try
        {
            var scope = System.Text.Json.JsonSerializer.Deserialize<DayScope>(day.AppliesToJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (scope is null) return true;
            if (scope.BranchIds is { Length: > 0 } && !scope.BranchIds.Contains(branchId)) return false;
            if (scope.DepartmentIds is { Length: > 0 } && (departmentId is null || !scope.DepartmentIds.Contains(departmentId.Value))) return false;
            return true;
        }
        catch (System.Text.Json.JsonException) { return true; }
    }

    private sealed record DayScope(Guid[]? BranchIds, Guid[]? DepartmentIds);

    /// <summary>Local→UTC that is deterministic in DST gaps/overlaps (NodaTime-style lenient mapping).</summary>
    private static DateTime SafeToUtc(TimeZoneInfo tz, DateTime local)
    {
        if (tz.IsInvalidTime(local)) local = local.AddMinutes(-tz.GetUtcOffset(local).TotalMinutes > 0 ? 60 : -60);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Impact preview for a draft publish: employees whose resolved shift/policy would change.</summary>
    public async Task<ImpactPreviewDto> PreviewShiftPublishAsync(Guid versionId, DateOnly effectiveFrom, CancellationToken ct)
    {
        var version = await db.ShiftVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        var employees = version is null ? []
            : await db.ShiftAssignments.AsNoTracking()
                .Where(a => a.ShiftId == version.ShiftId
                    && (a.EffectiveTo == null || a.EffectiveTo >= effectiveFrom))
                .Join(db.Employees.AsNoTracking(), a => a.EmployeeId, e => e.Id, (a, e) => new { a, e })
                .Select(x => new { x.e.Id, x.e.EmployeeCode, x.e.DisplayName, x.a.EffectiveFrom })
                .ToListAsync(ct);
        var sample = employees.Take(20)
            .Select(x => new ImpactPreviewRow(x.Id, x.EmployeeCode, x.DisplayName,
                effectiveFrom > x.EffectiveFrom ? effectiveFrom : x.EffectiveFrom, null, $"shift v{version?.Version}"))
            .ToList();
        return new ImpactPreviewDto(employees.Count, employees.Count * 30, sample);
    }

    public async Task<ImpactPreviewDto> PreviewPolicyPublishAsync(Guid versionId, DateOnly effectiveFrom, CancellationToken ct)
    {
        var version = await db.PolicyVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        var employees = version is null ? []
            : await db.PolicyAssignments.AsNoTracking()
                .Where(a => a.PolicyId == version.PolicyId
                    && (a.EffectiveTo == null || a.EffectiveTo >= effectiveFrom))
                .Join(db.Employees.AsNoTracking(), a => a.EmployeeId, e => e.Id, (a, e) => new { a, e })
                .Select(x => new { x.e.Id, x.e.EmployeeCode, x.e.DisplayName, x.a.EffectiveFrom })
                .ToListAsync(ct);
        var sample = employees.Take(20)
            .Select(x => new ImpactPreviewRow(x.Id, x.EmployeeCode, x.DisplayName,
                effectiveFrom > x.EffectiveFrom ? effectiveFrom : x.EffectiveFrom, null, $"policy v{version?.Version}"))
            .ToList();
        return new ImpactPreviewDto(employees.Count, employees.Count * 30, sample);
    }
}
