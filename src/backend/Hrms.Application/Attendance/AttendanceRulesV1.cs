using System.Text.Json;
using Hrms.Shared.Results;

namespace Hrms.Application.Attendance;

/// <summary>Typed attendance policy rules (schema 1). Stored as jsonb on PolicyVersion; every
/// numeric threshold lives here — the engine never hardcodes rule values.</summary>
public sealed record AttendanceRulesV1
{
    public int Schema { get; init; } = 1;
    public LateEarlyRules Late { get; init; } = new();
    public LateEarlyRules Early { get; init; } = new();
    public PenaltyRule[] LatePenalty { get; init; } = [];
    public PenaltyRule[] EarlyPenalty { get; init; } = [];
    public ThresholdRules Thresholds { get; init; } = new();
    public PunchRules Punch { get; init; } = new();
    public MissingPunchRules MissingPunch { get; init; } = new();
    public OvertimeRules Overtime { get; init; } = new();
    public RegularizationRules Regularization { get; init; } = new();
    public SelfPunchRules SelfPunch { get; init; } = new();
    public string PayableDaysBasis { get; init; } = "calendar";

    public sealed record LateEarlyRules
    {
        public int GraceMinutes { get; init; } = 10;
        public int MarkAfterMinutes { get; init; }
        public string CountFrom { get; init; } = "planned";
    }

    public sealed record PenaltyRule
    {
        public int Every { get; init; }
        public string Action { get; init; } = string.Empty;
    }

    public sealed record ThresholdRules
    {
        public int HalfDayBelowMinutes { get; init; } = 240;
        public int AbsentBelowMinutes { get; init; } = 120;
        public string Basis { get; init; } = "worked";
    }

    public sealed record PunchRules
    {
        public string Mode { get; init; } = "first_last";
        public int DuplicateWindowSeconds { get; init; } = 60;
        public int MinSessionMinutes { get; init; } = 5;
    }

    public sealed record MissingPunchRules
    {
        public string Status { get; init; } = "missing_punch";
        public string LockTreatment { get; init; } = "absent";
        public int ReminderAfterHours { get; init; } = 4;
    }

    public sealed record OvertimeRules
    {
        public bool Enabled { get; init; }
        public string[] EligibleEmploymentTypes { get; init; } = [];
        public string[] EligibleGradeCodes { get; init; } = [];
        public bool PreShift { get; init; }
        public int MinMinutes { get; init; } = 30;
        public int RoundingBlockMinutes { get; init; } = 15;
        public string RoundingMode { get; init; } = "floor";
        public int DailyCapMinutes { get; init; } = 240;
        public int WeeklyCapMinutes { get; init; } = 720;
        public int MonthlyCapMinutes { get; init; } = 3000;
        public string WorkingDayBand { get; init; } = "ot_1_5x";
        public string WeeklyOffBand { get; init; } = "ot_2x";
        public string HolidayBand { get; init; } = "ot_2x";
        public bool RequiresApproval { get; init; } = true;
        public string Disposition { get; init; } = "paid";
    }

    public sealed record RegularizationRules
    {
        public int WindowDays { get; init; } = 7;
        public int MaxPerPeriod { get; init; } = 4;
        public string HrApproval { get; init; } = "when_quota_exceeded";
        public int EscalateAfterDays { get; init; } = 3;
    }

    public sealed record SelfPunchRules
    {
        public bool Allowed { get; init; } = true;
        public bool GeofenceRequired { get; init; }
        public bool SelfieRequired { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AttendanceRulesV1 Defaults() => new();

    public static AttendanceRulesV1? Parse(string json) =>
        JsonSerializer.Deserialize<AttendanceRulesV1>(json, JsonOptions);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly string[] Modes = ["first_last", "pairs"];
    private static readonly string[] MissingStatuses = ["missing_punch", "absent", "half_day", "present"];
    private static readonly string[] LockTreatments = ["absent", "half_day", "lop", "present"];
    private static readonly string[] CountFroms = ["planned", "grace_end"];
    private static readonly string[] Bases = ["worked", "span"];
    private static readonly string[] Roundings = ["floor", "nearest", "ceil"];
    private static readonly string[] Dispositions = ["paid", "comp_off"];
    private static readonly string[] HrApprovals = ["always", "never", "when_quota_exceeded"];
    private static readonly string[] PenaltyActions = ["half_day_lop", "full_day_lop", "deduct_minutes"];
    private static readonly string[] PayableBases = ["calendar", "working"];

    /// <summary>Validates a rules object; returns error details keyed by json path.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(AttendanceRulesV1? r)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string path, string message)
        {
            if (!errors.TryGetValue(path, out var list)) errors[path] = list = [];
            list.Add(message);
        }

        if (r is null) { Add("$", "Rules are required."); return ToResult(errors); }
        if (r.Schema != 1) Add("schema", "Only rules schema 1 is supported.");
        if (r.Late.GraceMinutes is < 0 or > 240) Add("late.grace_minutes", "Grace must be 0-240 minutes.");
        if (r.Early.GraceMinutes is < 0 or > 240) Add("early.grace_minutes", "Grace must be 0-240 minutes.");
        if (!CountFroms.Contains(r.Late.CountFrom)) Add("late.count_from", $"count_from must be one of {string.Join(", ", CountFroms)}.");
        if (!CountFroms.Contains(r.Early.CountFrom)) Add("early.count_from", $"count_from must be one of {string.Join(", ", CountFroms)}.");
        if (r.Thresholds.HalfDayBelowMinutes is < 0 or > 1440) Add("thresholds.half_day_below_minutes", "Must be 0-1440.");
        if (r.Thresholds.AbsentBelowMinutes is < 0 or > 1440) Add("thresholds.absent_below_minutes", "Must be 0-1440.");
        if (r.Thresholds.AbsentBelowMinutes > r.Thresholds.HalfDayBelowMinutes)
            Add("thresholds.absent_below_minutes", "Must be <= half_day_below_minutes.");
        if (!Bases.Contains(r.Thresholds.Basis)) Add("thresholds.basis", $"basis must be one of {string.Join(", ", Bases)}.");
        if (!Modes.Contains(r.Punch.Mode)) Add("punch.mode", $"mode must be one of {string.Join(", ", Modes)}.");
        if (r.Punch.DuplicateWindowSeconds is < 0 or > 600) Add("punch.duplicate_window_seconds", "Must be 0-600.");
        if (r.Punch.MinSessionMinutes is < 0 or > 480) Add("punch.min_session_minutes", "Must be 0-480.");
        if (!MissingStatuses.Contains(r.MissingPunch.Status)) Add("missing_punch.status", $"status must be one of {string.Join(", ", MissingStatuses)}.");
        if (!LockTreatments.Contains(r.MissingPunch.LockTreatment)) Add("missing_punch.lock_treatment", $"lock_treatment must be one of {string.Join(", ", LockTreatments)}.");
        if (r.MissingPunch.ReminderAfterHours is < 0 or > 168) Add("missing_punch.reminder_after_hours", "Must be 0-168.");
        for (var i = 0; i < r.LatePenalty.Length; i++)
        {
            if (r.LatePenalty[i].Every < 1) Add($"late_penalty[{i}].every", "every must be >= 1.");
            if (!PenaltyActions.Contains(r.LatePenalty[i].Action)) Add($"late_penalty[{i}].action", $"action must be one of {string.Join(", ", PenaltyActions)}.");
        }
        for (var i = 0; i < r.EarlyPenalty.Length; i++)
        {
            if (r.EarlyPenalty[i].Every < 1) Add($"early_penalty[{i}].every", "every must be >= 1.");
            if (!PenaltyActions.Contains(r.EarlyPenalty[i].Action)) Add($"early_penalty[{i}].action", $"action must be one of {string.Join(", ", PenaltyActions)}.");
        }
        if (r.Overtime.MinMinutes is < 0 or > 480) Add("overtime.min_minutes", "Must be 0-480.");
        if (r.Overtime.RoundingBlockMinutes is < 0 or > 120) Add("overtime.rounding.block_minutes", "Must be 0-120.");
        if (!Roundings.Contains(r.Overtime.RoundingMode)) Add("overtime.rounding.mode", $"mode must be one of {string.Join(", ", Roundings)}.");
        if (r.Overtime.DailyCapMinutes is < 0 or > 1440) Add("overtime.caps.daily", "Must be 0-1440.");
        if (r.Overtime.WeeklyCapMinutes is < 0 or > 10080) Add("overtime.caps.weekly", "Must be 0-10080.");
        if (r.Overtime.MonthlyCapMinutes is < 0 or > 45000) Add("overtime.caps.monthly", "Must be 0-45000.");
        if (!Dispositions.Contains(r.Overtime.Disposition)) Add("overtime.disposition", $"disposition must be one of {string.Join(", ", Dispositions)}.");
        if (r.Regularization.WindowDays is < 0 or > 365) Add("regularization.window_days", "Must be 0-365.");
        if (r.Regularization.MaxPerPeriod is < 0 or > 31) Add("regularization.max_per_period", "Must be 0-31.");
        if (!HrApprovals.Contains(r.Regularization.HrApproval)) Add("regularization.hr_approval", $"hr_approval must be one of {string.Join(", ", HrApprovals)}.");
        if (r.Regularization.EscalateAfterDays is < 0 or > 90) Add("regularization.escalate_after_days", "Must be 0-90.");
        if (!PayableBases.Contains(r.PayableDaysBasis)) Add("payable_days_basis", $"payable_days_basis must be one of {string.Join(", ", PayableBases)}.");
        return ToResult(errors);
    }

    /// <summary>Parses + validates raw json. Returns the rules or a validation error with field paths.</summary>
    public static Result<AttendanceRulesV1> ParseValidated(string json)
    {
        AttendanceRulesV1? parsed;
        try { parsed = Parse(json); }
        catch (JsonException) { return Result<AttendanceRulesV1>.Failure(Error.Validation("rules.invalid", "Rules are not valid JSON.")); }
        var errors = Validate(parsed);
        if (errors.Count > 0)
        {
            return Result<AttendanceRulesV1>.Failure(Error.Validation("rules.invalid", "Attendance rules failed validation.", errors));
        }
        return parsed!;
    }

    private static IReadOnlyDictionary<string, string[]> ToResult(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(kv => kv.Key, kv => (string[]) [.. kv.Value]);
}
