using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Modules;
using Hrms.Testing;
using P = Hrms.Application.Modules.AttendancePermissions;

namespace Hrms.ApiTests;

/// <summary>M3.1: shift/policy version lifecycle, backdate gate, rules validation,
/// assignments + rosters, config resolver, entitlement + tenant isolation, audit.</summary>
[Collection(HrmsCollection.Name)]
public sealed class AttendanceMastersTests(HrmsFixture f)
{
    private static string Today(int add = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(add)).ToString("yyyy-MM-dd");
    private static string Next(DayOfWeek d)
    {
        var t = DateTime.UtcNow.Date;
        while (t.DayOfWeek != d) t = t.AddDays(1);
        return DateOnly.FromDateTime(t).ToString("yyyy-MM-dd");
    }

    private async Task EnableAsync(Guid tenantId)
    {
        using var platform = await f.PlatformAdminAsync();
        var r = await platform.Http.PutAsJsonAsync($"/api/v1/platform/tenants/{tenantId}/modules",
            new { moduleCode = AttendanceModule.ModuleCode, enabled = true, reason = "m3.1 test", validTo = (DateOnly?)null });
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> NewShiftAsync(HrmsClient admin, string? code = null)
    {
        var res = await admin.Http.PostAsJsonAsync("/api/v1/attendance/shifts",
            new { code = code ?? TestData.Unique("SH"), name = "Shift", isActive = true });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> NewVersionAsync(HrmsClient admin, Guid shiftId, string start = "09:00", string end = "18:00")
    {
        var res = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shifts/{shiftId}/versions", new
        {
            shiftType = "fixed", startTime = start, endTime = end, breakMinutes = 60, breakPaid = false,
            coreStart = (string?)null, coreEnd = (string?)null, requiredMinutes = (int?)null,
            graceInMinutes = (int?)null, graceOutMinutes = (int?)null,
            inWindowBeforeMinutes = (int?)null, outWindowAfterMinutes = (int?)null,
            otAllowed = false, weeklyOffConfigJson = (string?)null,
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement[]> VersionsAsync(HrmsClient admin, Guid shiftId) =>
        (await admin.GetJsonAsync($"/api/v1/attendance/shifts/{shiftId}/versions")).EnumerateArray().ToArray();

    [Fact]
    public async Task PublishClosesPriorVersionAndRejectsOverlap()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);

        var v1 = await NewVersionAsync(admin, shiftId);
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v1}/publish", new { effectiveFrom = Today() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var v2 = await NewVersionAsync(admin, shiftId, "10:00", "19:00");
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v2}/publish", new { effectiveFrom = Today(10) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var vs = await VersionsAsync(admin, shiftId);
        vs.Single(v => v.GetProperty("id").GetGuid() == v1).GetProperty("effectiveTo").GetString().ShouldBe(Today(9));
        vs.Single(v => v.GetProperty("id").GetGuid() == v2).GetProperty("status").GetString().ShouldBe("published");

        // Backfilling a window that starts before an existing published version is rejected.
        var v3 = await NewVersionAsync(admin, shiftId);
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v3}/publish", new { effectiveFrom = Today(5) }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PublishedVersionIsImmutableDraftEditable()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);
        var v1 = await NewVersionAsync(admin, shiftId);
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v1}/publish", new { effectiveFrom = Today() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var edit = await admin.Http.PutAsJsonAsync($"/api/v1/attendance/shift-versions/{v1}", new
        {
            shiftType = "fixed", startTime = "08:00", endTime = "17:00", breakMinutes = 60, breakPaid = false,
            otAllowed = false,
        });
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var draft = await NewVersionAsync(admin, shiftId);
        var ok = await admin.Http.PutAsJsonAsync($"/api/v1/attendance/shift-versions/{draft}", new
        {
            shiftType = "fixed", startTime = "08:00", endTime = "17:00", breakMinutes = 45, breakPaid = false,
            otAllowed = true,
        });
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BackdateRequiresDedicatedPermission()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);
        var v1 = await NewVersionAsync(admin, shiftId);

        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("AT"), name = "Att manager", description = (string?)null,
            permissions = new[] { new { permissionCode = P.MastersManage, scopeLevel = "Tenant" } },
        });
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);
        using (user)
        {
            (await user.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v1}/publish", new { effectiveFrom = Today(-5) }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v1}/publish", new { effectiveFrom = Today() }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Backdated publish on a fresh shift: user without the permission is denied,
        // the privileged admin succeeds.
        var shift2 = await NewShiftAsync(admin);
        var v2 = await NewVersionAsync(admin, shift2);
        var role2 = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("AT"), name = "Att manager 2", description = (string?)null,
            permissions = new[] { new { permissionCode = P.MastersManage, scopeLevel = "Tenant" } },
        });
        var (_, user2) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", role2);
        using (user2)
        {
            (await user2.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v2}/publish", new { effectiveFrom = Today(-5) }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{v2}/publish", new { effectiveFrom = Today(-5) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PolicyRulesValidationReturnsFieldPaths()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var policyId = await admin.PostForIdAsync("/api/v1/attendance/policies",
            new { code = TestData.Unique("POL"), name = "Policy", isDefault = false, isActive = true });

        var bad = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/policies/{policyId}/versions", new
        {
            rules = new { punch = new { mode = "bogus" }, late = new { countFrom = "bogus" } },
        });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errs = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errs.GetProperty("punch.mode").GetArrayLength().ShouldBeGreaterThan(0);
        errs.GetProperty("late.count_from").GetArrayLength().ShouldBeGreaterThan(0);

        var good = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/policies/{policyId}/versions", new { rules = new { schema = 1 } });
        good.StatusCode.ShouldBe(HttpStatusCode.OK, await good.Content.ReadAsStringAsync());
        var vid = (await good.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/policy-versions/{vid}/publish", new { effectiveFrom = Today() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShiftAssignmentsClosePriorAndRejectSameStart()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);
        var other = await NewShiftAsync(admin);
        var empId = await TestData.CreateEmployeeAsync(admin);

        var a1 = await admin.Http.PostAsJsonAsync("/api/v1/attendance/shift-assignments",
            new { employeeId = empId, shiftId, effectiveFrom = Today(), effectiveTo = (string?)null, reason = (string?)null });
        a1.StatusCode.ShouldBe(HttpStatusCode.OK, await a1.Content.ReadAsStringAsync());
        var a1Id = (await a1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Same start date as the open assignment is an overlap.
        (await admin.Http.PostAsJsonAsync("/api/v1/attendance/shift-assignments",
            new { employeeId = empId, shiftId = other, effectiveFrom = Today(), effectiveTo = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A later-start assignment closes the prior one.
        var a2 = await admin.Http.PostAsJsonAsync("/api/v1/attendance/shift-assignments",
            new { employeeId = empId, shiftId = other, effectiveFrom = Today(10), effectiveTo = (string?)null });
        a2.StatusCode.ShouldBe(HttpStatusCode.OK, await a2.Content.ReadAsStringAsync());
        (await a2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = (await admin.GetJsonAsync($"/api/v1/attendance/shift-assignments?employeeId={empId}")).EnumerateArray().ToArray();
        list.Single(a => a.GetProperty("id").GetGuid() == a1Id).GetProperty("effectiveTo").GetString().ShouldBe(Today(9));

        // Ending a1 inside its own closed window is allowed; extending past the next
        // assignment's start is rejected as an overlap.
        var clash = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-assignments/{a1Id}/end", new { effectiveTo = Today(20) });
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var end = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-assignments/{a1Id}/end", new { effectiveTo = Today(5) });
        end.StatusCode.ShouldBe(HttpStatusCode.NoContent, await end.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AssignmentScopeFiltersListForScopedUser()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);
        var empId = await TestData.CreateEmployeeAsync(admin);
        (await admin.Http.PostAsJsonAsync("/api/v1/attendance/shift-assignments",
            new { employeeId = empId, shiftId, effectiveFrom = Today(), effectiveTo = (string?)null }))
            .EnsureSuccessStatusCode();

        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("AT"), name = "Scoped", description = (string?)null,
            permissions = new[] { new { permissionCode = P.AssignmentsManage, scopeLevel = "Own" } },
        });
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);
        using (user)
        {
            var rows = await user.GetJsonAsync("/api/v1/attendance/shift-assignments");
            rows.EnumerateArray().ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task RosterBulkUpsertAndOffDays()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);
        var empId = await TestData.CreateEmployeeAsync(admin);

        var res = await admin.Http.PostAsJsonAsync("/api/v1/attendance/rosters", new
        {
            entries = new[]
            {
                new { employeeId = empId, workDate = Today(1), shiftId = (Guid?)shiftId, isOff = false, reason = "", source = "manual" },
                new { employeeId = empId, workDate = Today(2), shiftId = (Guid?)null, isOff = true, reason = "Rotational off", source = "manual" },
            },
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var counts = await res.Content.ReadFromJsonAsync<JsonElement>();
        counts.GetProperty("upserted").GetInt32().ShouldBe(2);
        counts.GetProperty("skipped").GetInt32().ShouldBe(0);

        var roster = (await admin.GetJsonAsync($"/api/v1/attendance/rosters?employeeId={empId}&from={Today()}&to={Today(5)}"))
            .EnumerateArray().ToArray();
        roster.Length.ShouldBe(2);
        roster.Single(r => r.GetProperty("workDate").GetString() == Today(2)).GetProperty("isOff").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ResolverPrecedenceAndOvernightInstants()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Att Co", legalName = (string?)null });
        var branchCode = TestData.Unique("B");
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = branchCode, name = "HQ" });

        // Overnight shift 22:00-06:00, Sundays weekly-off via branch pattern.
        var put = await admin.Http.PutAsJsonAsync($"/api/v1/org/branches/{branchId}", new
        {
            companyId, code = branchCode, name = "HQ", timeZone = "Asia/Kolkata",
            weeklyOffPattern = "fixed", weeklyOffConfigJson = """{"days":[0]}""",
        });
        put.IsSuccessStatusCode.ShouldBeTrue(await put.Content.ReadAsStringAsync());

        var empId = await admin.PostForIdAsync("/api/v1/employees", new
        {
            employeeCode = TestData.Unique("E"), displayName = "Night Emp", workEmail = (string?)null,
            branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null,
        });

        var shiftId = await NewShiftAsync(admin);
        var vId = await NewVersionAsync(admin, shiftId, "22:00", "06:00");
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/shift-versions/{vId}/publish", new { effectiveFrom = Today(-30) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Http.PostAsJsonAsync("/api/v1/attendance/shift-assignments",
            new { employeeId = empId, shiftId, effectiveFrom = Today(-30), effectiveTo = (string?)null }))
            .EnsureSuccessStatusCode();

        async Task<JsonElement> Resolve(string date) =>
            await admin.GetJsonAsync($"/api/v1/attendance/config/resolve?employeeId={empId}&date={date}");

        var wed = await Resolve(Next(DayOfWeek.Wednesday));
        wed.GetProperty("dayType").GetString().ShouldBe("working");
        wed.GetProperty("shiftId").GetGuid().ShouldBe(shiftId);
        wed.GetProperty("shiftSource").GetString()!.ShouldContain("assignment");
        var start = wed.GetProperty("plannedStart").GetDateTimeOffset();
        var end = wed.GetProperty("plannedEnd").GetDateTimeOffset();
        start.UtcDateTime.Hour.ShouldBe(16); start.UtcDateTime.Minute.ShouldBe(30); // 22:00 IST
        end.UtcDateTime.Hour.ShouldBe(0); end.UtcDateTime.Minute.ShouldBe(30);      // 06:00 IST next day
        end.Date.ShouldBe(start.Date.AddDays(1));

        (await Resolve(Next(DayOfWeek.Sunday))).GetProperty("dayType").GetString().ShouldBe("weekly_off");

        // Holiday on the calendar beats working.
        var calId = await admin.PostForIdAsync("/api/v1/attendance/calendars",
            new { code = TestData.Unique("CAL"), name = "Cal", year = (int?)null, isDefault = true, isActive = true });
        var mon = Next(DayOfWeek.Monday);
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/calendars/{calId}/days", new[]
        {
            new { date = mon, dayType = "holiday", name = "Test Holiday", halfDayMinutes = (int?)null, appliesToJson = (string?)null },
            new { date = Next(DayOfWeek.Saturday), dayType = "working_override", name = "Working Sat", halfDayMinutes = (int?)null, appliesToJson = (string?)null },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Resolve(mon)).GetProperty("dayType").GetString().ShouldBe("holiday");
        (await Resolve(Next(DayOfWeek.Saturday))).GetProperty("dayType").GetString().ShouldBe("working_override");

        // Rostered off beats everything.
        (await admin.Http.PostAsJsonAsync("/api/v1/attendance/rosters", new
        {
            entries = new[] { new { employeeId = empId, workDate = mon, shiftId = (Guid?)null, isOff = true, reason = "rotational", source = "manual" } },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Resolve(mon)).GetProperty("dayType").GetString().ShouldBe("roster_off");
    }

    [Fact]
    public async Task EntitlementOffDeniesModule()
    {
        using var globex = await f.GlobexAdminAsync();
        using var platform = await f.PlatformAdminAsync();
        var off = await platform.Http.PutAsJsonAsync($"/api/v1/platform/tenants/{f.GlobexId}/modules",
            new { moduleCode = AttendanceModule.ModuleCode, enabled = false, reason = "m3.1 test", validTo = (DateOnly?)null });
        off.IsSuccessStatusCode.ShouldBeTrue(await off.Content.ReadAsStringAsync());
        (await globex.Http.GetAsync("/api/v1/attendance/shifts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await EnableAsync(f.GlobexId);
        (await globex.Http.GetAsync("/api/v1/attendance/shifts")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CrossTenantWritesReturnNotFound()
    {
        await EnableAsync(f.AcmeId);
        await EnableAsync(f.GlobexId);
        using var acme = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(acme);

        using var globex = await f.GlobexAdminAsync();
        var upd = await globex.Http.PutAsJsonAsync($"/api/v1/attendance/shifts/{shiftId}", new { code = "X", name = "X", isActive = true });
        upd.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.Http.GetAsync($"/api/v1/attendance/shifts/{shiftId}/versions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AuditEntriesWrittenAndDataStaysInTenantDb()
    {
        await EnableAsync(f.AcmeId);
        await EnableAsync(f.GlobexId);
        using var admin = await f.AcmeAdminAsync();
        var shiftId = await NewShiftAsync(admin);

        var entries = (await admin.Http.GetFromJsonAsync<JsonElement[]>($"/api/v1/audit/entries?entityType=Shift&entityId={shiftId}"))!;
        entries.ShouldContain(e => e.GetProperty("action").GetString() == "create");

        var acmeDb = await f.TenantDatabaseAsync("acme");
        (await f.ScalarAsync<long>(acmeDb, "select count(*) from att.shifts where id = @i", ("i", (object)shiftId))).ShouldBe(1);
        var globexDb = await f.TenantDatabaseAsync("globex");
        (await f.ScalarAsync<long>(globexDb, "select count(*) from att.shifts where id = @i", ("i", (object)shiftId))).ShouldBe(0);
    }
}
