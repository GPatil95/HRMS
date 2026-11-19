using Hrms.Application.Attendance;
using Shouldly;

namespace Hrms.UnitTests;

/// <summary>M3.1: typed policy-rules validation — enum sets, ranges, field paths.</summary>
public sealed class AttendanceRulesTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        AttendanceRulesV1.Validate(AttendanceRulesV1.Defaults()).ShouldBeEmpty();
    }

    [Fact]
    public void NullRulesFailAtRoot()
    {
        var errors = AttendanceRulesV1.Validate(null);
        errors.ShouldContainKey("$");
    }

    [Fact]
    public void InvalidEnumsAndRangesReportJsonPaths()
    {
        var rules = AttendanceRulesV1.Defaults() with
        {
            Punch = new AttendanceRulesV1.PunchRules { Mode = "bogus" },
            Late = new AttendanceRulesV1.LateEarlyRules { CountFrom = "bogus", GraceMinutes = 999 },
            Overtime = new AttendanceRulesV1.OvertimeRules { RoundingMode = "bogus", MonthlyCapMinutes = -1 },
            MissingPunch = new AttendanceRulesV1.MissingPunchRules { LockTreatment = "bogus" },
            Regularization = new AttendanceRulesV1.RegularizationRules { HrApproval = "bogus" },
            PayableDaysBasis = "bogus",
        };
        var errors = AttendanceRulesV1.Validate(rules);
        errors.Keys.ShouldBe(new[]
        {
            "late.grace_minutes", "late.count_from", "punch.mode",
            "missing_punch.lock_treatment", "overtime.rounding.mode", "overtime.caps.monthly",
            "regularization.hr_approval", "payable_days_basis",
        }, ignoreOrder: true);
    }

    [Fact]
    public void PenaltyArraysAreValidatedPerIndex()
    {
        var rules = AttendanceRulesV1.Defaults() with
        {
            LatePenalty = [new AttendanceRulesV1.PenaltyRule { Every = 0, Action = "bogus" }],
        };
        var errors = AttendanceRulesV1.Validate(rules);
        errors.ShouldContainKey("late_penalty[0].every");
        errors.ShouldContainKey("late_penalty[0].action");
    }

    [Fact]
    public void AbsentThresholdMustNotExceedHalfDay()
    {
        var rules = AttendanceRulesV1.Defaults() with
        {
            Thresholds = new AttendanceRulesV1.ThresholdRules { HalfDayBelowMinutes = 100, AbsentBelowMinutes = 200 },
        };
        AttendanceRulesV1.Validate(rules).ShouldContainKey("thresholds.absent_below_minutes");
    }

    [Fact]
    public void ParseValidatedRejectsBadJsonAndBadValues()
    {
        AttendanceRulesV1.ParseValidated("{not json").IsSuccess.ShouldBeFalse();
        var bad = AttendanceRulesV1.ParseValidated("""{"schema":2}""");
        bad.IsSuccess.ShouldBeFalse();
        var good = AttendanceRulesV1.ParseValidated("""{"schema":1,"overtime":{"enabled":true,"minMinutes":15}}""");
        good.IsSuccess.ShouldBeTrue();
        good.Value.Overtime.MinMinutes.ShouldBe(15);
        good.Value.Punch.Mode.ShouldBe("first_last"); // defaults fill missing sections
    }

    [Fact]
    public void ToJsonRoundTripsThroughParse()
    {
        var r = AttendanceRulesV1.Defaults() with { PayableDaysBasis = "working" };
        var parsed = AttendanceRulesV1.Parse(r.ToJson());
        parsed!.PayableDaysBasis.ShouldBe("working");
        AttendanceRulesV1.Validate(parsed).ShouldBeEmpty();
    }
}
