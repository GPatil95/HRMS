using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class OrgMastersTests(HrmsFixture f)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAKUlEQVR4nO3NMQ0AAAgDsEmbNKSjgoOkSf9m2lMRCAQCgUAgEAgEX4IFd/4APakroPUAAAAASUVORK5CYII=");

    private static JsonElement Detail(JsonElement r) => r;

    private static async Task<HttpResponseMessage> PutJson(HrmsClient c, string url, object body) => await c.Http.PutAsJsonAsync(url, body);

    private static async Task<Guid> CompanyAsync(HrmsClient c) =>
        await c.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Org Masters Co", legalName = (string?)null });

    private static async Task<Guid> BranchAsync(HrmsClient c, Guid companyId) =>
        await c.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });

    private static async Task<Guid> DepartmentAsync(HrmsClient c, Guid branchId) =>
        await c.PostForIdAsync("/api/v1/org/departments", new { code = TestData.Unique("D"), name = "Engineering", branchId });

    private static async Task<Guid> EmployeeAsync(HrmsClient c, Guid branchId, Guid? deptId) =>
        await c.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "Approver One", workEmail = (string?)null, branchId, departmentId = deptId, managerEmployeeId = (Guid?)null });

    [Fact]
    public async Task CompanyDetailRoundTripsAllFields()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);

        var r = await PutJson(admin, $"/api/v1/org/companies/{id}", new
        {
            code = "OMC", name = "Org Masters Co Pvt Ltd", legalName = "Org Masters Co Private Limited",
            companyType = "private_limited", industry = "Software", incorporationDate = "2020-01-15",
            website = "https://omc.example", officialEmail = "info@omc.example", contactNumber = "+91 80 4000 0000",
            cin = "U72900KA2020PTC123456", pan = "AAACO1234F", tan = "BLRO12345F",
            pfEstablishmentCode = "KA/BNG/12345", esicEmployerCode = "52-1234567-000-0001", professionalTaxRegNo = "PT-123",
            shopsEstablishmentRegNo = "SH-55", factoryLicenceNo = (string?)null, msmeUdyamRegNo = "UDYAM-KR-07-0001234",
            currency = "INR", financialYearStartMonth = 4, payrollFrequency = "monthly", salaryPayDay = 7,
            defaultLeavePolicyCode = "LEAVE-STD", defaultHolidayCalendarCode = "HC-KA", defaultAttendancePolicyCode = "ATT-STD",
            defaultShiftName = "General 9-6", weeklyOffPattern = "fixed", weeklyOffConfigJson = """{"days":[0]}""",
            graceMinutes = 10, isActive = true,
        });
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());

        var d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        Detail(d).GetProperty("pan").GetString().ShouldBe("AAACO1234F");
        Detail(d).GetProperty("cin").GetString().ShouldBe("U72900KA2020PTC123456");
        Detail(d).GetProperty("currency").GetString().ShouldBe("INR");
        Detail(d).GetProperty("weeklyOffConfigJson").GetString()!.Contains("days").ShouldBeTrue();
        Detail(d).GetProperty("rowVersion").GetUInt32().ShouldBeGreaterThan(0u);
    }

    [Fact]
    public async Task CompanyValidationRejectsBadPanAndEmail()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);
        var r = await PutJson(admin, $"/api/v1/org/companies/{id}", new
        {
            code = "OKCO", name = "Ok Co", pan = "NOTAPAN", officialEmail = "not-an-email", isActive = true,
        });
        r.StatusCode.ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).ShouldContain("validation-failed");
    }

    [Fact]
    public async Task DuplicateCompanyCodeConflictsOnUpdate()
    {
        using var admin = await f.AcmeAdminAsync();
        var a = await admin.PostForIdAsync("/api/v1/org/companies", new { code = "DUPA", name = "A", legalName = (string?)null });
        var b = await admin.PostForIdAsync("/api/v1/org/companies", new { code = "DUPB", name = "B", legalName = (string?)null });
        (await PutJson(admin, $"/api/v1/org/companies/{b}", new { code = "DUPA", name = "B", isActive = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MultipleGstinsPerCompanySupported()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);
        await PutJson(admin, $"/api/v1/org/companies/{id}", new { code = "GSTCO", name = "Gst Co", pan = "AAACG1234P", isActive = true });

        var r1 = await admin.Http.PostAsJsonAsync($"/api/v1/org/companies/{id}/registrations",
            new { registrationType = "gstin", registrationNumber = "29AAACG1234P1Z5", registrationSubtype = "regular" });
        r1.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var r2 = await admin.Http.PostAsJsonAsync($"/api/v1/org/companies/{id}/registrations",
            new { registrationType = "gstin", registrationNumber = "27AAACG1234P1ZH", registrationSubtype = "regular" });
        r2.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var dup = await admin.Http.PostAsJsonAsync($"/api/v1/org/companies/{id}/registrations",
            new { registrationType = "gstin", registrationNumber = "29AAACG1234P1Z5" });
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var badPan = await admin.Http.PostAsJsonAsync($"/api/v1/org/companies/{id}/registrations",
            new { registrationType = "gstin", registrationNumber = "29ZZZZZ9999Z1Z5" });
        badPan.IsSuccessStatusCode.ShouldBeFalse();

        var d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        d.GetProperty("registrations").EnumerateArray().Count(x => x.GetProperty("registrationType").GetString() == "gstin").ShouldBe(2);
    }

    [Fact]
    public async Task AddressesAndContactsCrud()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);
        var aid = await admin.PostForIdAsync($"/api/v1/org/companies/{id}/addresses", new
        {
            addressType = "registered", line1 = "1st Main", line2 = "Indiranagar", city = "Bengaluru", state = "Karnataka", country = "India", pinCode = "560038",
        });
        var cid = await admin.PostForIdAsync($"/api/v1/org/companies/{id}/contacts", new
        {
            contactType = "signatory", name = "Dir One", designation = "Director", email = "dir@omc.test", phone = (string?)null,
        });

        var d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        d.GetProperty("addresses").GetArrayLength().ShouldBe(1);
        d.GetProperty("contacts").GetArrayLength().ShouldBe(1);

        (await admin.Http.DeleteAsync($"/api/v1/org/companies/{id}/addresses/{aid}")).EnsureSuccessStatusCode();
        (await admin.Http.DeleteAsync($"/api/v1/org/companies/{id}/contacts/{cid}")).EnsureSuccessStatusCode();
        d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        d.GetProperty("addresses").GetArrayLength().ShouldBe(0);
        d.GetProperty("contacts").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task BankAccountRequiresEditPermission()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);
        var bid = await admin.PostForIdAsync($"/api/v1/org/companies/{id}/bank-accounts",
            new { bankName = "HDFC", branchName = "MG Road", accountNumber = "50100234567890", ifsc = "HDFC0001234", isPrimary = true });
        var accounts = await admin.GetJsonAsync($"/api/v1/org/companies/{id}/bank-accounts");
        accounts.GetArrayLength().ShouldBe(1);

        var (_, viewer) = await f.CreateTenantUserAsync(admin, "acme", $"bank-{Guid.NewGuid():N}@acme.test");
        using (viewer)
        {
            (await viewer.Http.GetAsync($"/api/v1/org/companies/{id}/bank-accounts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task LogoUploadReplaceDownloadRemove()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);

        async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string name, string mime)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "file", name);
            return await admin.Http.PostAsync($"/api/v1/org/companies/{id}/logo", form);
        }

        var uploaded = await UploadAsync(Png, "logo.png", "image/png");
        uploaded.IsSuccessStatusCode.ShouldBeTrue(await uploaded.Content.ReadAsStringAsync());
        var fileId = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync()).RootElement.GetProperty("fileId").GetGuid();

        var d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        d.GetProperty("logoFileId").GetGuid().ShouldBe(fileId);

        var dl = await admin.Http.GetAsync($"/api/v1/org/companies/{id}/logo");
        dl.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await dl.Content.ReadAsByteArrayAsync()).ShouldBe(Png);

        // PNG content masquerading as JPEG is rejected by signature validation
        var bad = await UploadAsync(Png, "logo.jpg", "image/jpeg");
        bad.IsSuccessStatusCode.ShouldBeFalse();
        // non-image content rejected
        var bad2 = await UploadAsync("not an image"u8.ToArray(), "logo.png", "image/png");
        bad2.IsSuccessStatusCode.ShouldBeFalse();

        // replace
        var uploaded2 = await UploadAsync(Png, "logo.png", "image/png");
        uploaded2.EnsureSuccessStatusCode();
        var fileId2 = JsonDocument.Parse(await uploaded2.Content.ReadAsStringAsync()).RootElement.GetProperty("fileId").GetGuid();
        fileId2.ShouldNotBe(fileId);
        // old file is gone from the file service
        (await admin.Http.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // other tenant cannot read it
        using var globex = await f.GlobexAdminAsync();
        (await globex.Http.GetAsync($"/api/v1/org/companies/{id}/logo")).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        (await admin.Http.DeleteAsync($"/api/v1/org/companies/{id}/logo")).EnsureSuccessStatusCode();
        (await admin.Http.GetAsync($"/api/v1/org/companies/{id}/logo")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        d = await admin.GetJsonAsync($"/api/v1/org/companies/{id}");
        d.GetProperty("logoFileId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task BranchDetailRoundTrip()
    {
        using var admin = await f.AcmeAdminAsync();
        var cid = await CompanyAsync(admin);
        var bid = await BranchAsync(admin, cid);
        var emp = await EmployeeAsync(admin, bid, null);

        var r = await PutJson(admin, $"/api/v1/org/branches/{bid}", new
        {
            companyId = cid, code = "HQ", name = "Bangalore HQ", timeZone = "Asia/Kolkata", branchType = "head_office",
            openingDate = "2020-01-01", managerEmployeeId = emp, contactEmail = "hq@omc.test",
            addressLine1 = "MG Road", city = "Bengaluru", state = "Karnataka", country = "India", pinCode = "560001",
            workLocationType = "office", latitude = 12.97, longitude = 77.59, geofenceRadiusMeters = 200,
            defaultShiftName = "General", workStart = "09:00", workEnd = "18:00", breakMinutes = 60,
            weeklyOffPattern = "alternate_saturday", weeklyOffConfigJson = """{"nthWeekdays":[{"weekday":6,"weeks":[2,4]}]}""",
            holidayCalendarCode = "HC-KA", graceMinutes = 15, overtimeAllowed = true, attendanceMode = "geo",
            attendancePolicyCode = "ATT-1", payrollDay = 5, costCentre = "CC-100", isActive = true,
        });
        (await r.Content.ReadAsStringAsync()).ShouldBe(r.IsSuccessStatusCode ? "" : "err");

        var d = await admin.GetJsonAsync($"/api/v1/org/branches/{bid}");
        Detail(d).GetProperty("latitude").GetDouble().ShouldBe(12.97);
        Detail(d).GetProperty("managerName").GetString()!.Contains("Approver One").ShouldBeTrue();
        Detail(d).GetProperty("geofenceRadiusMeters").GetInt32().ShouldBe(200);
    }

    [Fact]
    public async Task DepartmentHodAssignTracksHistoryAndGuards()
    {
        using var admin = await f.AcmeAdminAsync();
        var cid = await CompanyAsync(admin);
        var bid = await BranchAsync(admin, cid);
        var dept = await DepartmentAsync(admin, bid);
        var e1 = await EmployeeAsync(admin, bid, dept);
        var e2 = await EmployeeAsync(admin, bid, dept);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assign = await admin.Http.PostAsJsonAsync($"/api/v1/org/departments/{dept}/hod",
            new { role = "hod", employeeId = e1, effectiveFrom = today.ToString("yyyy-MM-dd") });
        assign.EnsureSuccessStatusCode();

        // reassign with earlier effective date is rejected while an assignment is open
        var stale = await admin.Http.PostAsJsonAsync($"/api/v1/org/departments/{dept}/hod",
            new { role = "hod", employeeId = e2, effectiveFrom = today.ToString("yyyy-MM-dd") });
        stale.IsSuccessStatusCode.ShouldBeFalse();

        var next = await admin.Http.PostAsJsonAsync($"/api/v1/org/departments/{dept}/hod",
            new { role = "hod", employeeId = e2, effectiveFrom = today.AddDays(30).ToString("yyyy-MM-dd"), reason = "Reorg" });
        next.EnsureSuccessStatusCode();

        // same person as HOD and alt HOD rejected
        var same = await admin.Http.PostAsJsonAsync($"/api/v1/org/departments/{dept}/hod",
            new { role = "alt_hod", employeeId = e2, effectiveFrom = today.AddDays(31).ToString("yyyy-MM-dd") });
        same.IsSuccessStatusCode.ShouldBeFalse();

        var history = await admin.GetJsonAsync($"/api/v1/org/departments/{dept}/assignments");
        var rows = history.EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows.First().GetProperty("employeeId").GetGuid().ShouldBe(e2);
        rows.Last().GetProperty("effectiveTo").GetDateTime().ShouldBe(new DateTime(today.AddDays(29), TimeOnly.MinValue));

        var d = await admin.GetJsonAsync($"/api/v1/org/departments/{dept}");
        d.GetProperty("hodEmployeeId").GetGuid().ShouldBe(e2);
    }

    [Fact]
    public async Task CircularDepartmentHierarchyRejected()
    {
        using var admin = await f.AcmeAdminAsync();
        var cid = await CompanyAsync(admin);
        var bid = await BranchAsync(admin, cid);
        var parent = await DepartmentAsync(admin, bid);
        var child = await admin.PostForIdAsync("/api/v1/org/departments", new { code = TestData.Unique("D"), name = "Child", branchId = bid });
        var grandchild = await admin.PostForIdAsync("/api/v1/org/departments", new { code = TestData.Unique("D"), name = "Grand", branchId = bid });
        (await PutJson(admin, $"/api/v1/org/departments/{child}", new { code = "CHILD", name = "Child", branchId = bid, parentDepartmentId = parent, isActive = true })).EnsureSuccessStatusCode();
        (await PutJson(admin, $"/api/v1/org/departments/{grandchild}", new { code = "GRAND", name = "Grand", branchId = bid, parentDepartmentId = child, isActive = true })).EnsureSuccessStatusCode();

        // making parent a child of grandchild would close a loop
        var r = await PutJson(admin, $"/api/v1/org/departments/{parent}", new { code = "P", name = "Parent", parentDepartmentId = grandchild, isActive = true });
        r.StatusCode.ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).ShouldContain("circular");
    }

    [Fact]
    public async Task EffectiveConfigPrecedence()
    {
        using var admin = await f.AcmeAdminAsync();
        var cid = await CompanyAsync(admin);
        var bid = await BranchAsync(admin, cid);
        var dept = await DepartmentAsync(admin, bid);
        var emp = await EmployeeAsync(admin, bid, dept);

        await PutJson(admin, $"/api/v1/org/companies/{cid}", new
        {
            code = "PREC", name = "Prec Co", graceMinutes = 5, weeklyOffPattern = "fixed", weeklyOffConfigJson = """{"days":[0]}""",
            defaultHolidayCalendarCode = "HC-IN", isActive = true,
        });
        await PutJson(admin, $"/api/v1/org/branches/{bid}", new
        {
            companyId = cid, code = "PB", name = "PB", timeZone = "Asia/Kolkata", graceMinutes = 20, isActive = true,
        });

        // employee resolves: grace from branch, weekly off + holiday calendar from company
        var cfg = await admin.GetJsonAsync($"/api/v1/org/employees/{emp}/effective-config");
        var settings = cfg.GetProperty("settings").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!);
        settings["graceMinutes"].GetProperty("source").GetString().ShouldBe("branch");
        settings["graceMinutes"].GetProperty("value").GetInt32().ShouldBe(20);
        settings["weeklyOffPattern"].GetProperty("source").GetString().ShouldBe("company");
        settings["holidayCalendar"].GetProperty("value").GetString().ShouldBe("HC-IN");

        // department explicit override wins over branch + company
        await PutJson(admin, $"/api/v1/org/departments/{dept}", new
        {
            code = "PD", name = "PD", branchId = bid, graceMinutes = 30, weeklyOffPattern = "fixed",
            weeklyOffConfigJson = """{"days":[0,6]}""", isActive = true,
        });
        cfg = await admin.GetJsonAsync($"/api/v1/org/departments/{dept}/effective-config");
        settings = cfg.GetProperty("settings").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!);
        settings["graceMinutes"].GetProperty("source").GetString().ShouldBe("department");
        settings["graceMinutes"].GetProperty("isOverride").GetBoolean().ShouldBeTrue();
        settings["weeklyOffConfig"].GetProperty("value").GetString()!.Contains('6').ShouldBeTrue();

        // weekly-off preview walks the effective (department) config
        var preview = await admin.GetJsonAsync($"/api/v1/org/departments/{dept}/weekly-off-preview?from=2026-01-01&to=2026-01-11");
        preview.GetProperty("offDays").EnumerateArray().Select(x => x.GetString()).ShouldBe(["2026-01-03", "2026-01-04", "2026-01-10", "2026-01-11"]);
    }

    [Fact]
    public async Task OrgEndpointsRequirePermissions()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await CompanyAsync(admin);
        var (_, viewer) = await f.CreateTenantUserAsync(admin, "acme", $"orgv-{Guid.NewGuid():N}@acme.test");
        using (viewer)
        {
            (await viewer.Http.GetAsync($"/api/v1/org/companies/{id}")).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        }
    }
}
