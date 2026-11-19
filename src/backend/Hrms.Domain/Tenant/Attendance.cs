using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

/// <summary>Version lifecycle shared by shift and policy versions.</summary>
public static class AttendanceVersionStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";
}

public static class ShiftTypes
{
    public const string Fixed = "fixed";
    public const string Flexi = "flexi";
    public const string Open = "open";
    public static readonly string[] All = [Fixed, Flexi, Open];
}

public static class CalendarDayTypes
{
    public const string Holiday = "holiday";
    public const string OptionalHoliday = "optional_holiday";
    /// <summary>Compensatory working day (e.g. a Saturday worked to compensate a shutdown).</summary>
    public const string WorkingOverride = "working_override";
    public const string HalfDay = "half_day";
    public static readonly string[] All = [Holiday, OptionalHoliday, WorkingOverride, HalfDay];
}

/// <summary>Shift identity — stable code; all behavioural fields live on <see cref="ShiftVersion"/>.</summary>
[Audited("attendance")]
public sealed class Shift : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Effective-dated, immutable-once-published shift behaviour. Overlap between published
/// windows is DB-enforced (EXCLUDE gist, added by the AttendanceMasters migration).</summary>
[Audited("attendance")]
public sealed class ShiftVersion : AuditableEntity
{
    public Guid ShiftId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = AttendanceVersionStatuses.Draft;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string ShiftType { get; set; } = ShiftTypes.Fixed;
    /// <summary>Local wall-clock start/end; crosses_midnight is derived (end &lt;= start).</summary>
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public bool BreakPaid { get; set; }
    /// <summary>Flexi only: core presence window; null when unused.</summary>
    public TimeOnly? CoreStart { get; set; }
    public TimeOnly? CoreEnd { get; set; }
    /// <summary>Flexi/open: required worked minutes; fixed: derived from span − unpaid break.</summary>
    public int? RequiredMinutes { get; set; }
    /// <summary>null → falls back to the policy version's late/early grace.</summary>
    public int? GraceInMinutes { get; set; }
    public int? GraceOutMinutes { get; set; }
    /// <summary>Earliest valid check-in before planned start (default 120).</summary>
    public int InWindowBeforeMinutes { get; set; } = 120;
    /// <summary>Latest valid check-out after planned end (default 240).</summary>
    public int OutWindowAfterMinutes { get; set; } = 240;
    public bool OtAllowed { get; set; } = true;
    /// <summary>Weekly-off config used only when the org weekly-off pattern is 'shift_based'
    /// (same JSON shape as WeeklyOffConfig in WeeklyOffRules).</summary>
    public string? WeeklyOffConfigJson { get; set; }
}

/// <summary>Holiday/work calendar — only exceptions are stored as rows; weekly offs stay rule-based.</summary>
[Audited("attendance")]
public sealed class WorkCalendar : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Year { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

[Audited("attendance")]
public sealed class CalendarDay : AuditableEntity
{
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public string DayType { get; set; } = CalendarDayTypes.Holiday;
    public string Name { get; set; } = string.Empty;
    /// <summary>For half_day: reduced planned minutes.</summary>
    public int? HalfDayMinutes { get; set; }
    /// <summary>null = applies to everyone; otherwise {"branch_ids":[…],"department_ids":[…]}.</summary>
    public string? AppliesToJson { get; set; }
}

[Audited("attendance")]
public sealed class AttendancePolicy : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Effective-dated, immutable-once-published rule set (rules_json = AttendanceRulesV1).</summary>
[Audited("attendance")]
public sealed class PolicyVersion : AuditableEntity
{
    public Guid PolicyId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = AttendanceVersionStatuses.Draft;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string RulesJson { get; set; } = string.Empty;
}

/// <summary>Effective-dated employee ↔ shift assignment; no-overlap DB-enforced.</summary>
[Audited("attendance")]
public sealed class ShiftAssignment : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public string Source { get; set; } = "manual";
}

/// <summary>Optional explicit employee ↔ policy override; otherwise the policy resolves via org codes.</summary>
[Audited("attendance")]
public sealed class PolicyAssignment : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Guid PolicyId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Per-day roster override (rotating shifts, planned offs, swaps). Wins over every
/// other day-type source except approved leave (future Leave module).</summary>
[Audited("attendance")]
public sealed class AttendanceRoster : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    /// <summary>null + IsOff = explicitly rostered off.</summary>
    public Guid? ShiftId { get; set; }
    public bool IsOff { get; set; }
    public string? Reason { get; set; }
    public string Source { get; set; } = "manual";
}
