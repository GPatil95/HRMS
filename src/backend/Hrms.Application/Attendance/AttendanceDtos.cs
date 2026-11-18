using Hrms.Domain.Tenant;

namespace Hrms.Application.Attendance;

public sealed record ShiftDto(Guid Id, string Code, string Name, bool IsActive, int VersionCount,
    int? ActiveVersion, DateOnly? ActiveFrom, DateOnly? ActiveTo);
public sealed record ShiftVersionDto(Guid Id, Guid ShiftId, int Version, string Status,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string ShiftType,
    TimeOnly StartTime, TimeOnly EndTime, bool CrossesMidnight, int BreakMinutes, bool BreakPaid,
    TimeOnly? CoreStart, TimeOnly? CoreEnd, int? RequiredMinutes,
    int? GraceInMinutes, int? GraceOutMinutes, int InWindowBeforeMinutes, int OutWindowAfterMinutes,
    bool OtAllowed, string? WeeklyOffConfigJson);
public sealed record ShiftVersionInput(string ShiftType, TimeOnly StartTime, TimeOnly EndTime,
    int BreakMinutes, bool BreakPaid, TimeOnly? CoreStart, TimeOnly? CoreEnd, int? RequiredMinutes,
    int? GraceInMinutes, int? GraceOutMinutes, int? InWindowBeforeMinutes, int? OutWindowAfterMinutes,
    bool OtAllowed, string? WeeklyOffConfigJson);
public sealed record ShiftInput(string Code, string Name, bool IsActive);

public sealed record WorkCalendarDto(Guid Id, string Code, string Name, int? Year, bool IsDefault,
    bool IsActive, int DayCount);
public sealed record WorkCalendarInput(string Code, string Name, int? Year, bool IsDefault, bool IsActive);
public sealed record CalendarDayDto(Guid Id, DateOnly Date, string DayType, string Name,
    int? HalfDayMinutes, string? AppliesToJson);
public sealed record CalendarDayInput(DateOnly Date, string DayType, string Name,
    int? HalfDayMinutes, string? AppliesToJson);

public sealed record AttendancePolicyDto(Guid Id, string Code, string Name, bool IsDefault, bool IsActive,
    int VersionCount, int? ActiveVersion);
public sealed record AttendancePolicyInput(string Code, string Name, bool IsDefault, bool IsActive);
public sealed record PolicyVersionDto(Guid Id, Guid PolicyId, int Version, string Status,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, AttendanceRulesV1 Rules);
public sealed record PolicyVersionInput(AttendanceRulesV1 Rules);

public sealed record ShiftAssignmentDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName,
    Guid ShiftId, string ShiftCode, string ShiftName, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? Reason, string Source);
public sealed record ShiftAssignmentInput(Guid EmployeeId, Guid ShiftId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? Reason);
public sealed record PolicyAssignmentDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName,
    Guid PolicyId, string PolicyCode, string PolicyName, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Reason);
public sealed record PolicyAssignmentInput(Guid EmployeeId, Guid PolicyId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? Reason);

public sealed record RosterEntry(Guid EmployeeId, DateOnly WorkDate, Guid? ShiftId, bool IsOff,
    string? Reason, string Source);
public sealed record RosterUpsertInput(IReadOnlyList<RosterEntry> Entries);
public sealed record RosterBulkResult(int Upserted, int Skipped);

public sealed record ImpactPreviewDto(int AffectedEmployees, int NewDaysChanged,
    IReadOnlyList<ImpactPreviewRow> Sample);
public sealed record ImpactPreviewRow(Guid EmployeeId, string EmployeeCode, string DisplayName,
    DateOnly Date, string? Before, string? After);

public sealed record ResolvedAttendanceDto(
    Guid EmployeeId, DateOnly Date, string TimeZone,
    string DayType, // working | weekly_off | holiday | optional_holiday | working_override | half_day | roster_off
    Guid? ShiftId, string? ShiftCode, int? ShiftVersion, string? ShiftSource,
    Guid? PolicyId, string? PolicyCode, int? PolicyVersion, string? PolicySource,
    Guid? CalendarId, string? CalendarCode, string? CalendarSource,
    DateTimeOffset? PlannedStart, DateTimeOffset? PlannedEnd,
    int? PlannedMinutes, int GraceInMinutes, int GraceOutMinutes,
    string? OrgSource); // description of the org path used

internal static class AttendanceMappings
{
    public static ShiftDto ToDto(this Shift s, int versionCount, ShiftVersion? active) =>
        new(s.Id, s.Code, s.Name, s.IsActive, versionCount, active?.Version, active?.EffectiveFrom, active?.EffectiveTo);

    public static ShiftVersionDto ToDto(this ShiftVersion v) =>
        new(v.Id, v.ShiftId, v.Version, v.Status, v.EffectiveFrom, v.EffectiveTo, v.ShiftType,
            v.StartTime, v.EndTime, v.EndTime <= v.StartTime, v.BreakMinutes, v.BreakPaid,
            v.CoreStart, v.CoreEnd, v.RequiredMinutes, v.GraceInMinutes, v.GraceOutMinutes,
            v.InWindowBeforeMinutes, v.OutWindowAfterMinutes, v.OtAllowed, v.WeeklyOffConfigJson);

    public static WorkCalendarDto ToDto(this WorkCalendar c, int dayCount) =>
        new(c.Id, c.Code, c.Name, c.Year, c.IsDefault, c.IsActive, dayCount);

    public static CalendarDayDto ToDto(this CalendarDay d) =>
        new(d.Id, d.Date, d.DayType, d.Name, d.HalfDayMinutes, d.AppliesToJson);

    public static AttendancePolicyDto ToDto(this AttendancePolicy p, int versionCount, PolicyVersion? active) =>
        new(p.Id, p.Code, p.Name, p.IsDefault, p.IsActive, versionCount, active?.Version);

    public static PolicyVersionDto ToDto(this PolicyVersion v) =>
        new(v.Id, v.PolicyId, v.Version, v.Status, v.EffectiveFrom, v.EffectiveTo,
            AttendanceRulesV1.Parse(v.RulesJson) ?? AttendanceRulesV1.Defaults());
}
