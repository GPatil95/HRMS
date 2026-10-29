using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrms.Application.Organization;

/// <summary>
/// Weekly-off configuration stored as JSON on company/branch/department. Weekdays use <see cref="DayOfWeek"/> numbering (0 = Sunday).
/// </summary>
public sealed record WeeklyOffConfig
{
    /// <summary>Weekdays that are always off (fixed / multiple weekdays).</summary>
    public int[] Days { get; init; } = [];

    /// <summary>Nth-weekday rules, e.g. 2nd and 4th Saturday: { weekday: 6, weeks: [2, 4] }.</summary>
    public NthWeekdayRule[] NthWeekdays { get; init; } = [];

    /// <summary>Rotating pattern: days counted from <c>Anchor</c> in cycles of <c>CycleDays</c>; offsets listed are off.</summary>
    public RotationRule? Rotation { get; init; }

    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
}

public sealed record NthWeekdayRule(int Weekday, int[] Weeks);

public sealed record RotationRule(DateOnly Anchor, int CycleDays, int[] OffOffsets);

public static class WeeklyOffPatterns
{
    public const string Fixed = "fixed";
    public const string AlternateSaturday = "alternate_saturday";
    public const string NthWeekday = "nth_weekday";
    public const string Rotating = "rotating";
    public const string ShiftBased = "shift_based";

    public static readonly string[] All = [Fixed, AlternateSaturday, NthWeekday, Rotating, ShiftBased];
}

public static class WeeklyOffRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Validates a pattern + config pair. Returns an error message or null when valid.</summary>
    public static string? Validate(string? pattern, string? configJson)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.IsNullOrWhiteSpace(configJson) ? null : "Weekly-off configuration requires a pattern.";
        }

        if (!WeeklyOffPatterns.All.Contains(pattern))
        {
            return $"Unknown weekly-off pattern '{pattern}'.";
        }

        WeeklyOffConfig? cfg;
        try
        {
            cfg = Parse(configJson);
        }
        catch (JsonException)
        {
            return "Weekly-off configuration is not valid JSON.";
        }

        if (pattern == WeeklyOffPatterns.ShiftBased)
        {
            return null;
        }

        if (cfg is null)
        {
            return "Weekly-off configuration is required for this pattern.";
        }

        if (cfg.Days.Any(d => d is < 0 or > 6) || cfg.Days.Distinct().Count() != cfg.Days.Length)
        {
            return "Weekly-off days must be distinct weekdays 0-6.";
        }

        foreach (var r in cfg.NthWeekdays)
        {
            if (r.Weekday is < 0 or > 6 || r.Weeks.Length == 0 || r.Weeks.Any(w => w is < 1 or > 5))
            {
                return "Nth-weekday rules need a weekday 0-6 and weeks between 1 and 5.";
            }
        }

        if (cfg.EffectiveFrom is { } f && cfg.EffectiveTo is { } t && t < f)
        {
            return "Weekly-off effective-to date must be on or after effective-from.";
        }

        switch (pattern)
        {
            case WeeklyOffPatterns.Fixed when cfg.Days.Length == 0:
                return "Select at least one weekly-off day.";
            case WeeklyOffPatterns.AlternateSaturday or WeeklyOffPatterns.NthWeekday when cfg.NthWeekdays.Length == 0:
                return "Add at least one nth-weekday rule.";
            case WeeklyOffPatterns.Rotating:
                if (cfg.Rotation is not { } rot || rot.CycleDays is < 2 or > 60 || rot.OffOffsets.Length == 0
                    || rot.OffOffsets.Any(o => o < 0 || o >= rot.CycleDays) || rot.OffOffsets.Length >= rot.CycleDays)
                {
                    return "Rotating patterns need a cycle of 2-60 days and off offsets inside the cycle.";
                }

                break;
        }

        return null;
    }

    public static WeeklyOffConfig? Parse(string? configJson) =>
        string.IsNullOrWhiteSpace(configJson) ? null : JsonSerializer.Deserialize<WeeklyOffConfig>(configJson, Json);

    public static string? Normalize(string? configJson) =>
        Parse(configJson) is { } cfg ? JsonSerializer.Serialize(cfg, Json) : null;

    /// <summary>True when <paramref name="date"/> is a weekly off. Shift-based patterns are resolved by the attendance module, so return false.</summary>
    public static bool IsWeeklyOff(string? pattern, WeeklyOffConfig? cfg, DateOnly date)
    {
        if (cfg is null || string.IsNullOrWhiteSpace(pattern) || pattern == WeeklyOffPatterns.ShiftBased)
        {
            return false;
        }

        if ((cfg.EffectiveFrom is { } f && date < f) || (cfg.EffectiveTo is { } t && date > t))
        {
            return false;
        }

        var dow = (int)date.DayOfWeek;
        if (cfg.Days.Contains(dow))
        {
            return true;
        }

        var weekOfMonth = ((date.Day - 1) / 7) + 1;
        if (cfg.NthWeekdays.Any(r => r.Weekday == dow && r.Weeks.Contains(weekOfMonth)))
        {
            return true;
        }

        if (pattern == WeeklyOffPatterns.Rotating && cfg.Rotation is { } rot && rot.CycleDays > 0)
        {
            var diff = date.DayNumber - rot.Anchor.DayNumber;
            var offset = ((diff % rot.CycleDays) + rot.CycleDays) % rot.CycleDays;
            return rot.OffOffsets.Contains(offset);
        }

        return false;
    }

    public static List<DateOnly> OffDays(string? pattern, string? configJson, DateOnly from, DateOnly to)
    {
        var cfg = Parse(configJson);
        var result = new List<DateOnly>();
        for (var d = from; d <= to && result.Count < 400; d = d.AddDays(1))
        {
            if (IsWeeklyOff(pattern, cfg, d))
            {
                result.Add(d);
            }
        }

        return result;
    }
}
