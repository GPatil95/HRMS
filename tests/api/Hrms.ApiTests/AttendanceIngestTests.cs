using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hrms.Application.Modules;
using Hrms.Testing;
using P = Hrms.Application.Modules.AttendancePermissions;

namespace Hrms.ApiTests;

/// <summary>M3.2: device registration/auth, raw punch ingestion, dedup + idempotency,
/// mappings + back-fill, manual/self punches, voids, CSV import, scope + tenant isolation.</summary>
[Collection(HrmsCollection.Name)]
public sealed class AttendanceIngestTests(HrmsFixture f)
{
    private async Task EnableAsync(Guid tenantId)
    {
        using var platform = await f.PlatformAdminAsync();
        var r = await platform.Http.PutAsJsonAsync($"/api/v1/platform/tenants/{tenantId}/modules",
            new { moduleCode = AttendanceModule.ModuleCode, enabled = true, reason = "m3.2 test", validTo = (DateOnly?)null });
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());
    }

    private static async Task<(Guid Id, string Token)> NewDeviceAsync(HrmsClient admin, string? tzMode = "utc", string? tz = null)
    {
        var res = await admin.Http.PostAsJsonAsync("/api/v1/attendance/devices", new
        {
            code = TestData.Unique("D"), name = "Bio device", vendor = "zk",
            branchId = (Guid?)null, protocol = "push_api",
            timezoneMode = tzMode, deviceTimezone = tz, isActive = true,
        });
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var j = JsonDocument.Parse(body).RootElement;
        return (j.GetProperty("device").GetProperty("id").GetGuid(), j.GetProperty("token").GetString()!);
    }

    private async Task<HttpResponseMessage> PushAsync(string host, string deviceCode, string token, string idemKey, object punches)
    {
        using var c = f.Client(host);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attendance/devices/{deviceCode}/punches")
        { Content = JsonContent.Create(new { punches }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Device", token);
        req.Headers.Add("Idempotency-Key", idemKey);
        return await c.Http.SendAsync(req);
    }

    private static async Task<string> DeviceCodeAsync(HrmsClient admin, Guid id) =>
        (await admin.GetJsonAsync("/api/v1/attendance/devices"))
            .EnumerateArray().First(d => d.GetProperty("id").GetGuid() == id)
            .GetProperty("code").GetString()!;

    private static JsonElement Result(HttpResponseMessage r) =>
        JsonDocument.Parse(r.Content.ReadAsStringAsync().Result).RootElement;

    private static object Punch(string code, string ts, string dir = "in") =>
        new { deviceUserCode = code, timestamp = ts, direction = dir };

    private async Task<string> AcmeDb() => await f.TenantDatabaseAsync("acme");

    // ---------- Device lifecycle + auth ----------

    [Fact]
    public async Task DeviceTokenAuthenticatesPushAndUpdatesLastSeen()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var (id, token) = await NewDeviceAsync(admin);
        var code = await DeviceCodeAsync(admin, id);

        var r = await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}",
            new[] { Punch("42", "2026-11-20T09:00:00Z") });
        r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var res = Result(r);
        res.GetProperty("accepted").GetInt32().ShouldBe(1);

        var device = (await admin.GetJsonAsync("/api/v1/attendance/devices"))
            .EnumerateArray().First(d => d.GetProperty("id").GetGuid() == id);
        device.GetProperty("lastSeenAt").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task BadInactiveAndCrossTenantTokensAreRejected()
    {
        await EnableAsync(f.AcmeId);
        await EnableAsync(f.GlobexId);
        using var admin = await f.AcmeAdminAsync();
        var (id, token) = await NewDeviceAsync(admin);
        var code = await DeviceCodeAsync(admin, id);

        (await PushAsync("acme.localhost", code, "dev_wrong", $"k-{Guid.NewGuid():N}", new[] { Punch("1", "2026-11-20T09:00:00Z") }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Acme token on the globex host: the hash only exists in acme's DB.
        (await PushAsync("globex.localhost", code, token, $"k-{Guid.NewGuid():N}", new[] { Punch("1", "2026-11-20T09:00:00Z") }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Deactivate → rejected; rotate → old token dead, new works.
        await admin.Http.PutAsJsonAsync($"/api/v1/attendance/devices/{id}",
            new { code = code, name = "Bio device", isActive = false });
        (await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}", new[] { Punch("1", "2026-11-20T09:00:00Z") }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await admin.Http.PutAsJsonAsync($"/api/v1/attendance/devices/{id}",
            new { code = code, name = "Bio device", isActive = true });
        var rot = await admin.Http.PostAsync($"/api/v1/attendance/devices/{id}/rotate-token", null);
        var newToken = JsonDocument.Parse(await rot.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString()!;
        (await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}", new[] { Punch("1", "2026-11-20T09:00:00Z") }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PushAsync("acme.localhost", code, newToken, $"k-{Guid.NewGuid():N}", new[] { Punch("1", "2026-11-20T09:00:00Z") }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- Idempotency + dedup ----------

    [Fact]
    public async Task SameKeyReplaysSamePayloadAndRejectsDifferentPayload()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var (deviceId, token) = await NewDeviceAsync(admin);
        var code = await DeviceCodeAsync(admin, deviceId);
        var key = $"k-{Guid.NewGuid():N}";
        var uc = TestData.Unique("U");
        var punches = new[] { Punch(uc, "2026-11-20T08:00:00Z"), Punch(uc, "2026-11-20T17:30:00Z", "out") };

        var first = await PushAsync("acme.localhost", code, token, key, punches);
        var b1 = Result(first).GetProperty("batchId").GetString();

        // Exact retry: same batch, no extra punches.
        var retry = await PushAsync("acme.localhost", code, token, key, punches);
        Result(retry).GetProperty("batchId").GetString().ShouldBe(b1);

        // Same key, different payload → 409.
        var altered = await PushAsync("acme.localhost", code, token, key,
            new[] { Punch(uc, "2026-11-21T08:00:00Z") });
        altered.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var count = await f.ScalarAsync<long>(await AcmeDb(),
            $"select count(*) from att.punches where device_user_code = '{uc}'");
        count.ShouldBe(2);
    }

    [Fact]
    public async Task SameContentUnderNewKeyCountsAsDuplicates()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var (deviceId, token) = await NewDeviceAsync(admin);
        var code = await DeviceCodeAsync(admin, deviceId);
        var uc = TestData.Unique("U");
        var punches = new[] { Punch(uc, "2026-11-20T08:00:00Z") };

        await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}", punches);
        var second = await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}", punches);
        var res = Result(second);
        res.GetProperty("accepted").GetInt32().ShouldBe(0);
        res.GetProperty("duplicates").GetInt32().ShouldBe(1);

        (await f.ScalarAsync<long>(await AcmeDb(),
            $"select count(*) from att.punches where device_user_code = '{uc}'")).ShouldBe(1);
    }

    // ---------- Mapping + back-fill ----------

    [Fact]
    public async Task UnmappedPunchesAreStoredAndBackfilledWhenMapped()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var (deviceId, token) = await NewDeviceAsync(admin);
        var code = await DeviceCodeAsync(admin, deviceId);
        var empId = await TestData.CreateEmployeeAsync(admin);
        var uc = TestData.Unique("U");

        await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}",
            new[] { Punch(uc, "2026-11-20T09:15:00Z") });

        // Unmapped: stored with employee_id NULL (visible via the raw viewer too).
        (await f.ScalarAsync<long>(await AcmeDb(),
            $"select count(*) from att.punches where employee_id is null and device_user_code = '{uc}'")).ShouldBe(1);

        var map = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/devices/{deviceId}/user-map",
            new { deviceUserCode = uc, employeeId = empId, effectiveFrom = "2026-11-01", effectiveTo = (string?)null });
        map.StatusCode.ShouldBe(HttpStatusCode.OK, await map.Content.ReadAsStringAsync());
        await f.DrainAsync(); // runs attendance.device.map

        (await f.ScalarAsync<long>(await AcmeDb(),
            $"select count(*) from att.punches where employee_id is null and device_user_code = '{uc}'")).ShouldBe(0);
        var listed = (await admin.GetJsonAsync($"/api/v1/attendance/punches?unmappedOnly=true&deviceId={deviceId}"))
            .GetProperty("totalCount").GetInt32();
        listed.ShouldBe(0);

        // Overlapping mapping for same device+code is rejected (exclusion constraint).
        var dup = await admin.Http.PostAsJsonAsync($"/api/v1/attendance/devices/{deviceId}/user-map",
            new { deviceUserCode = uc, employeeId = empId, effectiveFrom = "2026-11-05", effectiveTo = (string?)null });
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------- Timezone handling ----------

    [Fact]
    public async Task DeviceLocalTimestampsConvertToUtcAndPreserveWallTime()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var (deviceId, token) = await NewDeviceAsync(admin, "device_local", "Asia/Kolkata");
        var code = await DeviceCodeAsync(admin, deviceId);
        var uc = TestData.Unique("U");

        var push = await PushAsync("acme.localhost", code, token, $"k-{Guid.NewGuid():N}",
            new[] { Punch(uc, "2026-11-20 09:30:00") }); // naive local time, IST = UTC+5:30
        push.StatusCode.ShouldBe(HttpStatusCode.OK, await push.Content.ReadAsStringAsync());

        var row = await f.ScalarAsync<DateTime>(await AcmeDb(),
            $"select punched_at from att.punches where device_user_code = '{uc}'");
        row.ShouldBe(new DateTime(2026, 11, 20, 4, 0, 0)); // IST 09:30 → UTC 04:00
        var wall = await f.ScalarAsync<DateTime>(await AcmeDb(),
            $"select local_wall_time from att.punches where device_user_code = '{uc}'");
        wall.ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
    }

    // ---------- Manual / self / void ----------

    [Fact]
    public async Task ManualPunchRequiresNoteAndPermissionAndCanBeVoided()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var empId = await TestData.CreateEmployeeAsync(admin);

        (await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
            new { employeeId = empId, punchedAt = "2026-11-20T09:00:00Z", direction = "in", note = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var created = await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
            new { employeeId = empId, punchedAt = "2026-11-20T09:00:00Z", direction = "in", note = "Forgot card" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var punchId = Result(created).GetProperty("id").GetGuid();

        // A user without attendance.punches.manual cannot create or void.
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("R"), name = "View only", description = (string?)null,
            permissions = new[] { new { permissionCode = P.View, scopeLevel = "Tenant" } },
        });
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);
        using (user)
        {
            (await user.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
                new { employeeId = empId, punchedAt = "2026-11-20T10:00:00Z", note = "x" }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.PostAsJsonAsync($"/api/v1/attendance/punches/{punchId}/void",
                new { reason = "dup" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/punches/{punchId}/void",
            new { reason = "entered twice" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.PostAsJsonAsync($"/api/v1/attendance/punches/{punchId}/void",
            new { reason = "again" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SelfPunchRecordsOwnEmployeeAndGeo()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var empId = await TestData.CreateEmployeeAsync(admin);

        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("R"), name = "Self", description = (string?)null,
            permissions = new[] { new { permissionCode = P.MyPunch, scopeLevel = "Tenant" }, new { permissionCode = P.MyView, scopeLevel = "Tenant" } },
        });
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);
        using (user)
        {
            (await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/user", new { userId }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var r = await user.Http.PostAsJsonAsync("/api/v1/attendance/punches/self",
                new { direction = "in", clientTime = (string?)null, geoLat = 18.52, geoLng = 73.85, geoAccuracyM = 20, note = (string?)null });
            r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            var mine = await user.GetJsonAsync("/api/v1/attendance/punches/my");
            mine.GetProperty("totalCount").GetInt32().ShouldBe(1);
        }
    }

    /// <summary>The seeded EMPLOYEE role carries attendance.my.* so every employee can punch and
    /// read their own records — while the raw-punch list, manual entry, voiding and import all
    /// stay behind admin permissions (cross-employee access is impossible by design: /punches/my
    /// is hard-filtered to the caller's linked employee).</summary>
    [Fact]
    public async Task EmployeeRoleGrantsOwnAttendanceAndDeniesOtherRecordsAndAdminOps()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var ownEmp = await TestData.CreateEmployeeAsync(admin);
        var otherEmp = await TestData.CreateEmployeeAsync(admin);
        await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
            new { employeeId = otherEmp, punchedAt = "2026-11-20T09:00:00Z", direction = "in", note = "t" });

        var employeeRoleId = (await admin.GetJsonAsync("/api/v1/roles")).EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "EMPLOYEE").GetProperty("id").GetGuid();
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", employeeRoleId);
        using (user)
        {
            (await admin.Http.PutAsJsonAsync($"/api/v1/employees/{ownEmp}/user", new { userId }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Own records: punch + read succeed with the seeded role alone.
            (await user.Http.PostAsJsonAsync("/api/v1/attendance/punches/self", new { direction = "in" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            var mine = await user.GetJsonAsync("/api/v1/attendance/punches/my");
            mine.GetProperty("totalCount").GetInt32().ShouldBe(1);
            mine.GetProperty("items").EnumerateArray().First().GetProperty("employeeId").GetGuid().ShouldBe(ownEmp);

            // Other employees' records and admin operations stay denied.
            (await user.Http.GetAsync("/api/v1/attendance/punches")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.GetAsync($"/api/v1/attendance/punches?employeeId={otherEmp}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
                new { employeeId = otherEmp, punchedAt = "2026-11-20T10:00:00Z", direction = "in", note = "x" }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.PostAsJsonAsync($"/api/v1/attendance/punches/{Guid.NewGuid()}/void", new { reason = "x" }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    // ---------- Viewer scope ----------

    [Fact]
    public async Task PunchViewerEnforcesDataScope()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var empId = await TestData.CreateEmployeeAsync(admin);
        await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
            new { employeeId = empId, punchedAt = "2026-11-20T09:00:00Z", direction = "in", note = "t" });

        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("R"), name = "None", description = (string?)null,
            permissions = new[] { new { permissionCode = P.View, scopeLevel = "Own" } },
        });
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);
        using (user)
        {
            var res = await user.GetJsonAsync("/api/v1/attendance/punches");
            res.GetProperty("totalCount").GetInt32().ShouldBe(0);
        }
    }

    // ---------- CSV import ----------

    [Fact]
    public async Task CsvImportPartialAcceptsAndReplaysByFile()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var emp = await TestData.CreateEmployeeAsync(admin);
        var empCode = (await admin.GetJsonAsync($"/api/v1/employees/{emp}")).GetProperty("employeeCode").GetString()!;

        var csv = $"code,ts,dir,emp\n" +
                  $"A1,2026-11-20T09:00:00Z,in,{empCode}\n" +
                  $"A2,not-a-date,in,{empCode}\n" +
                  $"A3,2026-11-20T17:00:00Z,out,GHOST-EMP\n";
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "punches.csv");
        form.Add(new StringContent(P.PunchImportPurpose), "purpose");
        var upload = await admin.Http.PostAsync("/api/v1/files", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var fileId = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var imp = await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/import", new
        {
            fileId, deviceId = (Guid?)null,
            mapping = new { deviceUserCodeCol = 0, timestampCol = 1, directionCol = 2, employeeCodeCol = 3 },
            idempotencyKey = (string?)null, skipHeader = true,
        });
        imp.StatusCode.ShouldBe(HttpStatusCode.OK, await imp.Content.ReadAsStringAsync());
        var res = Result(imp);
        res.GetProperty("accepted").GetInt32().ShouldBe(1);
        res.GetProperty("rejected").GetInt32().ShouldBe(2);
        var batchId = res.GetProperty("batchId").GetString();

        // Re-import of the same file replays the batch.
        var again = await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/import", new
        {
            fileId, deviceId = (Guid?)null,
            mapping = new { deviceUserCodeCol = 0, timestampCol = 1, directionCol = 2, employeeCodeCol = 3 },
            idempotencyKey = (string?)null, skipHeader = true,
        });
        Result(again).GetProperty("batchId").GetString().ShouldBe(batchId);
        (await f.ScalarAsync<long>(await AcmeDb(),
            $"select count(*) from att.punches where ingest_batch_id = '{batchId}'")).ShouldBe(1);
    }

    // ---------- Append-only guarantee ----------

    [Fact]
    public async Task PunchRowsCannotBeUpdatedOrDeletedExceptBackfill()
    {
        await EnableAsync(f.AcmeId);
        using var admin = await f.AcmeAdminAsync();
        var empId = await TestData.CreateEmployeeAsync(admin);
        await admin.Http.PostAsJsonAsync("/api/v1/attendance/punches/manual",
            new { employeeId = empId, punchedAt = "2026-11-20T09:00:00Z", direction = "in", note = "t" });

        var db = await AcmeDb();
        var updateErr = await RunSqlExpectError(db,
            "update att.punches set note = 'tampered'");
        updateErr.ShouldNotBeNull();
        var deleteErr = await RunSqlExpectError(db, "delete from att.punches");
        deleteErr.ShouldNotBeNull();
        var updateEmpErr = await RunSqlExpectError(db,
            "update att.punches set employee_id = null");
        updateEmpErr.ShouldNotBeNull(); // populated → null is forbidden too
    }

    private async Task<string?> RunSqlExpectError(string db, string sql)
    {
        try
        {
            await f.ScalarAsync<object>(db, sql + " returning 1");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
