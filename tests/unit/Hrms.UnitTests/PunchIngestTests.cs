using System.Text.Json;
using Hrms.Application.Attendance;
using Hrms.Application.Organization;
using Hrms.Domain.Tenant;
using Shouldly;

namespace Hrms.UnitTests;

/// <summary>M3.2: timestamp normalization, dedup-key stability and device-local timezone handling.</summary>
public sealed class PunchIngestTests
{
    private static AttendanceDevice Device(string mode, string? tz = null) =>
        new() { TimezoneMode = mode, DeviceTimezone = tz };

    [Fact]
    public void UtcModeParsesOffsetsToUtcInstant()
    {
        var r = PunchIngestService.NormalizeTimestamp("2026-11-20T09:30:00+05:30", Device(DeviceTimezoneModes.Utc));
        r.IsSuccess.ShouldBeTrue();
        r.Value.Instant.Offset.ShouldBe(TimeSpan.Zero);
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 20, 4, 0, 0));
    }

    [Fact]
    public void UtcModeAssumesUtcForNaiveTimestamps()
    {
        var r = PunchIngestService.NormalizeTimestamp("2026-11-20 09:30:00", Device(DeviceTimezoneModes.Utc));
        r.IsSuccess.ShouldBeTrue();
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
    }

    [Fact]
    public void DeviceLocalConvertsByDeviceTimezone()
    {
        var r = PunchIngestService.NormalizeTimestamp("2026-11-20 09:30:00",
            Device(DeviceTimezoneModes.DeviceLocal, "Asia/Kolkata"));
        r.IsSuccess.ShouldBeTrue();
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 20, 4, 0, 0));
        r.Value.WallTime.ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
        r.Value.WallTime.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public void AmbiguousLocalTimeResolvesToFirstOccurrence()
    {
        // 2026-11-01 01:30 in America/New_York occurs twice (DST fall-back): UTC-4 then UTC-5.
        var r = PunchIngestService.NormalizeTimestamp("2026-11-01 01:30:00",
            Device(DeviceTimezoneModes.DeviceLocal, "America/New_York"));
        r.IsSuccess.ShouldBeTrue();
        r.Value.Instant.Offset.ShouldBe(TimeSpan.FromHours(-4));
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 1, 5, 30, 0));
    }

    [Fact]
    public void InvalidLocalTimeInDstGapShiftsForward()
    {
        // 2026-03-08 02:30 in America/New_York does not exist (spring forward).
        var r = PunchIngestService.NormalizeTimestamp("2026-03-08 02:30:00",
            Device(DeviceTimezoneModes.DeviceLocal, "America/New_York"));
        r.IsSuccess.ShouldBeTrue();
        r.Value.WallTime.ShouldBe(new DateTime(2026, 3, 8, 2, 30, 0)); // raw wall time preserved
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 3, 8, 7, 30, 0)); // 03:30 EDT
    }

    [Fact]
    public void DeviceLocalHonoursExplicitOffsetAndProjectsWallTime()
    {
        // A device that sends an offset-qualified stamp keeps its absolute instant;
        // the wall time is projected into the device timezone, not rejected.
        var r = PunchIngestService.NormalizeTimestamp("2026-11-20T09:30:00+05:30",
            Device(DeviceTimezoneModes.DeviceLocal, "Asia/Kolkata"));
        r.IsSuccess.ShouldBeTrue();
        r.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 20, 4, 0, 0));
        r.Value.WallTime.ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
        // An offset that disagrees with the device zone still yields the same instant.
        var r2 = PunchIngestService.NormalizeTimestamp("2026-11-20T05:00:00+01:00",
            Device(DeviceTimezoneModes.DeviceLocal, "Asia/Kolkata"));
        r2.IsSuccess.ShouldBeTrue();
        r2.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 20, 4, 0, 0));
        r2.Value.WallTime.ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
    }

    [Fact]
    public void OffsetInputDuringDstFallbackKeepsReportedInstant()
    {
        // 2026-11-01 01:30 in America/New_York exists twice (EDT -4, then EST -5).
        // Both occurrences must keep their own instant — collapsing them to one
        // would merge two distinct punches as duplicates.
        var daylight = PunchIngestService.NormalizeTimestamp("2026-11-01T01:30:00-04:00",
            Device(DeviceTimezoneModes.DeviceLocal, "America/New_York"));
        var standard = PunchIngestService.NormalizeTimestamp("2026-11-01T01:30:00-05:00",
            Device(DeviceTimezoneModes.DeviceLocal, "America/New_York"));
        daylight.IsSuccess.ShouldBeTrue();
        standard.IsSuccess.ShouldBeTrue();
        daylight.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 1, 5, 30, 0));
        standard.Value.Instant.UtcDateTime.ShouldBe(new DateTime(2026, 11, 1, 6, 30, 0));
        daylight.Value.WallTime.ShouldBe(new DateTime(2026, 11, 1, 1, 30, 0));
        standard.Value.WallTime.ShouldBe(new DateTime(2026, 11, 1, 1, 30, 0));
    }

    [Fact]
    public void BadAndMissingTimestampsAreRejected()
    {
        PunchIngestService.NormalizeTimestamp(null, Device(DeviceTimezoneModes.Utc)).IsSuccess.ShouldBeFalse();
        PunchIngestService.NormalizeTimestamp("not-a-date", Device(DeviceTimezoneModes.Utc)).IsSuccess.ShouldBeFalse();
        PunchIngestService.NormalizeTimestamp("garbage", Device(DeviceTimezoneModes.DeviceLocal, "Asia/Kolkata")).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void DedupKeyIsStableAndDistinguishesPhysicalEvent()
    {
        var dev = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);
        var k1 = PunchIngestService.BuildDedupKey(PunchSources.Device, dev, "E1", at);
        k1.ShouldBe(PunchIngestService.BuildDedupKey(PunchSources.Device, dev, "E1", at));
        k1.ShouldNotBe(PunchIngestService.BuildDedupKey(PunchSources.Device, dev, "E2", at));
        k1.ShouldNotBe(PunchIngestService.BuildDedupKey(PunchSources.Device, dev, "E1", at.AddSeconds(1)));
        k1.ShouldNotBe(PunchIngestService.BuildDedupKey(PunchSources.Device, Guid.NewGuid(), "E1", at));
        // The channel is ignored for device rows: a CSV offline-sync of the same physical
        // punch dedups against the device push.
        k1.ShouldBe(PunchIngestService.BuildDedupKey(PunchSources.Import, dev, "E1", at));
        // Source still distinguishes non-device rows (e.g. self vs manual at the same instant).
        PunchIngestService.BuildDedupKey(PunchSources.Self, null, "E1", at)
            .ShouldNotBe(PunchIngestService.BuildDedupKey(PunchSources.Manual, null, "E1", at));
    }
}

/// <summary>M3.1-deferred coverage: rotating cycle, nth-weekday and boundary-date weekly-off rules
/// plus the 'shift_based' evaluation path (config carried by the resolved shift version).</summary>
public sealed class WeeklyOffRuleTests
{
    private static WeeklyOffConfig Parse(WeeklyOffConfig cfg) =>
        JsonSerializer.Deserialize<WeeklyOffConfig>(JsonSerializer.Serialize(cfg))!;

    [Fact]
    public void RotatingCycleOffsetsRepeatAndWrapNegative()
    {
        // 4-day cycle: anchor day off, next day off, 2 working days (offsets 0 and 1 off).
        var cfg = Parse(new WeeklyOffConfig
        {
            Rotation = new RotationRule(new DateOnly(2026, 11, 16), 4, [0, 1]),
        });
        var anchor = new DateOnly(2026, 11, 16); // Monday
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor).ShouldBeTrue();      // offset 0
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(1)).ShouldBeTrue();  // offset 1
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(2)).ShouldBeFalse();
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(3)).ShouldBeFalse();
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(4)).ShouldBeTrue();  // wrap
        // Before the anchor the cycle wraps negatively — offset must never go negative.
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(-1)).ShouldBeFalse(); // offset 3
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(-2)).ShouldBeFalse();
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Rotating, cfg, anchor.AddDays(-4)).ShouldBeTrue();
    }

    [Fact]
    public void NthWeekdayHitsExactOccurrencesOnly()
    {
        // 2nd and 4th Saturday of each month.
        var cfg = Parse(new WeeklyOffConfig { NthWeekdays = [new NthWeekdayRule(6, [2, 4])] });
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, cfg, new DateOnly(2026, 11, 14)).ShouldBeTrue();  // 2nd Sat Nov 2026
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, cfg, new DateOnly(2026, 11, 28)).ShouldBeTrue();  // 4th Sat
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, cfg, new DateOnly(2026, 11, 7)).ShouldBeFalse();  // 1st Sat
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, cfg, new DateOnly(2026, 11, 21)).ShouldBeFalse(); // 3rd Sat
        // Month with no 5th Saturday: 5th-Saturday rule never fires.
        var fifth = Parse(new WeeklyOffConfig { NthWeekdays = [new NthWeekdayRule(6, [5])] });
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, fifth, new DateOnly(2026, 11, 28)).ShouldBeFalse();
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.NthWeekday, fifth, new DateOnly(2027, 1, 30)).ShouldBeTrue(); // 5th Sat Jan 2027
    }

    [Fact]
    public void EffectiveRangeBoundsRules()
    {
        var cfg = Parse(new WeeklyOffConfig
        {
            Days = [0],
            EffectiveFrom = new DateOnly(2026, 12, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
        });
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Fixed, cfg, new DateOnly(2026, 12, 6)).ShouldBeTrue();   // Sunday in range
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Fixed, cfg, new DateOnly(2026, 11, 29)).ShouldBeFalse(); // Sunday before range
        WeeklyOffRules.IsWeeklyOff(WeeklyOffPatterns.Fixed, cfg, new DateOnly(2027, 1, 4)).ShouldBeFalse();   // after range
    }

    [Fact]
    public void ShiftBasedEvaluatesEveryRuleTypeFromShiftConfig()
    {
        // 'shift_based': the resolved ShiftVersion's WeeklyOffConfigJson is the source — days, nth-weekday and rotation all apply.
        var cfg = Parse(new WeeklyOffConfig
        {
            Days = [0],
            NthWeekdays = [new NthWeekdayRule(6, [2])],
            Rotation = new RotationRule(new DateOnly(2026, 11, 16), 7, [0]),
        });
        WeeklyOffRules.IsOffByConfig(cfg, new DateOnly(2026, 11, 22)).ShouldBeTrue();  // Sunday (Days)
        WeeklyOffRules.IsOffByConfig(cfg, new DateOnly(2026, 11, 14)).ShouldBeTrue();  // 2nd Saturday (Nth)
        WeeklyOffRules.IsOffByConfig(cfg, new DateOnly(2026, 11, 23)).ShouldBeTrue();  // rotation offset 0 (Monday)
        WeeklyOffRules.IsOffByConfig(cfg, new DateOnly(2026, 11, 24)).ShouldBeFalse();
        WeeklyOffRules.IsOffByConfig(null, new DateOnly(2026, 11, 22)).ShouldBeFalse(); // unresolved shift → not off
    }

    [Fact]
    public void ValidationCatchesBadRotationAndMixedConfig()
    {
        WeeklyOffRules.Validate(WeeklyOffPatterns.ShiftBased, null).ShouldBeNull(); // config lives on the shift version
        WeeklyOffRules.Validate(WeeklyOffPatterns.Rotating,
            JsonSerializer.Serialize(new WeeklyOffConfig { Rotation = new RotationRule(new DateOnly(2026, 1, 1), 1, [0]) }))
            .ShouldNotBeNull(); // cycle < 2
        WeeklyOffRules.Validate(WeeklyOffPatterns.Rotating,
            JsonSerializer.Serialize(new WeeklyOffConfig { Rotation = new RotationRule(new DateOnly(2026, 1, 1), 4, [0, 1, 2, 3]) }))
            .ShouldNotBeNull(); // every day off = cycle meaningless
        WeeklyOffRules.Validate(WeeklyOffPatterns.Fixed, "{\"days\":[7]}").ShouldNotBeNull();
    }
}
