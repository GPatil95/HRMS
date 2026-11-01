using System.Net;
using System.Net.Http.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class EmployeeProfileTests(HrmsFixture f)
{
    private async Task<(HrmsClient client, Guid employeeId)> NewEmployeeAsync(string tenant = "acme")
    {
        var admin = await f.TenantLoginAsync(tenant, $"admin@{tenant}.test");
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Profile Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "Section Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        return (admin, empId);
    }

    [Fact]
    public async Task CreateAutoGeneratesCodeAndOpensHistory()
    {
        var (admin, empId) = await NewEmployeeAsync();
        var emp = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        emp.GetProperty("employeeCode").GetString()!.ShouldStartWith("EMP");

        var history = await admin.GetJsonAsync($"/api/v1/employees/{empId}/history");
        var rows = history.EnumerateArray().ToList();
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("eventType").GetString().ShouldBe("initial_assignment");
        rows[0].GetProperty("effectiveTo").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task PersonalRoundTripsWithRowVersion()
    {
        var (admin, empId) = await NewEmployeeAsync();
        var put = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/personal", new
        {
            dateOfBirth = "1990-05-15", gender = "male", maritalStatus = "married", nationality = "Indian",
            bloodGroup = "B+", personalEmail = "p@home.test", personalPhone = "9000000001",
            currentAddress = new { line1 = "1 Main St", city = "Pune", pin = "411001" },
            permanentAddress = (object?)null,
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var got = await admin.GetJsonAsync($"/api/v1/employees/{empId}/personal");
        got.GetProperty("bloodGroup").GetString().ShouldBe("B+");
        got.GetProperty("currentAddress").GetProperty("city").GetString().ShouldBe("Pune");
        var rv = got.GetProperty("rowVersion").GetUInt32();

        // Stale row version → 409.
        var stale = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/personal", new { dateOfBirth = (string?)null, gender = (string?)null, maritalStatus = (string?)null, nationality = (string?)null, bloodGroup = (string?)null, personalEmail = (string?)null, personalPhone = (string?)null, currentAddress = (object?)null, permanentAddress = (object?)null, rowVersion = rv - 1 });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NomineeShareCappedAt100AndPrimaryEmergencyDemotes()
    {
        var (admin, empId) = await NewEmployeeAsync();

        var n1 = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/contacts",
            new { kind = "nominee", name = "Spouse", relation = "spouse", dateOfBirth = (string?)null, phone = (string?)null, email = (string?)null, isPrimary = false, nomineeSharePct = 60 });
        n1.StatusCode.ShouldBe(HttpStatusCode.OK);

        var over = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/contacts",
            new { kind = "nominee", name = "Child", relation = "child", dateOfBirth = (string?)null, phone = (string?)null, email = (string?)null, isPrimary = false, nomineeSharePct = 50 });
        over.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var e1 = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/contacts",
            new { kind = "emergency", name = "Dad", relation = "father", dateOfBirth = (string?)null, phone = "111", email = (string?)null, isPrimary = true, nomineeSharePct = (int?)null });
        e1.StatusCode.ShouldBe(HttpStatusCode.OK);

        var e2 = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/contacts",
            new { kind = "emergency", name = "Mom", relation = "mother", dateOfBirth = (string?)null, phone = "222", email = (string?)null, isPrimary = true, nomineeSharePct = (int?)null });
        e2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await admin.GetJsonAsync($"/api/v1/employees/{empId}/contacts");
        var emergency = list.EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "emergency").ToList();
        emergency.Count(c => c.GetProperty("isPrimary").GetBoolean()).ShouldBe(1); // old primary demoted
    }

    [Fact]
    public async Task BankAccountMasksReplacesAndVerifies()
    {
        var (admin, empId) = await NewEmployeeAsync();

        var add = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/bank-accounts",
            new { bankName = "HDFC", branchName = "Pune", accountNumber = "50100123456789", ifsc = "HDFC0001234", holderName = "Section Emp", isPrimary = true, effectiveFrom = "2025-01-01" });
        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acct = await add.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var acctId = acct.GetProperty("id").GetGuid();
        acct.GetProperty("accountMasked").GetString().ShouldBe("****6789");
        acct.GetProperty("accountMasked").GetString()!.ShouldNotContain("1234"); // only last4

        // Replacement closes the old primary.
        var rep = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/bank-accounts/{acctId}/replace",
            new { bankName = "ICICI", branchName = "Pune", accountNumber = "98765000123456", ifsc = "ICIC0009876", holderName = "Section Emp", isPrimary = true, effectiveFrom = "2025-06-01" });
        rep.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await admin.GetJsonAsync($"/api/v1/employees/{empId}/bank-accounts");
        var rows = list.EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows.Count(r => r.GetProperty("isPrimary").GetBoolean() && r.GetProperty("effectiveTo").ValueKind == System.Text.Json.JsonValueKind.Null).ShouldBe(1);
        rows.Single(r => r.GetProperty("isPrimary").GetBoolean() && r.GetProperty("effectiveTo").ValueKind == System.Text.Json.JsonValueKind.Null)
            .GetProperty("bankName").GetString().ShouldBe("ICICI");

        var ver = await admin.Http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/employees/{empId}/bank-accounts/{acctId}/verify")
        { Content = JsonContent.Create(new { status = "verified", note = "penny-drop ok" }) });
        ver.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StatutoryEncryptsAndMasksPanAndValidates()
    {
        var (admin, empId) = await NewEmployeeAsync();

        var bad = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/statutory",
            new { pan = "BADPAN", uan = (string?)null, pfNumber = (string?)null, pfApplicable = true, esiNumber = (string?)null, esiApplicable = false, ptState = (string?)null, lwfApplicable = false, aadhaarLast4 = (string?)null, passport = (string?)null, taxRegime = "new_regime" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/statutory",
            new { pan = "ABCDE1234F", uan = "100012345678", pfNumber = "PU/PUN/1234567/000/0001234", pfApplicable = true, esiNumber = (string?)null, esiApplicable = false, ptState = "MH", lwfApplicable = true, aadhaarLast4 = "1234", passport = "A1234567", taxRegime = "new_regime" });
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var got = await admin.GetJsonAsync($"/api/v1/employees/{empId}/statutory");
        got.GetProperty("panMasked").GetString().ShouldBe("****234F");
        got.GetProperty("passportMasked").GetString().ShouldBe("****4567");
        got.ToString().ShouldNotContain("ABCDE1234F");
        got.ToString().ShouldNotContain("A1234567");

        // Stored value is ciphertext, never plaintext.
        var db = await f.TenantDatabaseAsync("acme");
        var raw = await f.ScalarAsync<string>(db, "select pan_encrypted from hr.employee_statutory where employee_id = @e", ("e", empId));
        raw.ShouldNotBeNull();
        raw!.ShouldNotContain("ABCDE1234F");
    }

    [Fact]
    public async Task SensitiveSectionsRequireTheirPermissions()
    {
        var (admin, empId) = await NewEmployeeAsync();
        var (_, viewer) = await f.CreateTenantUserAsync(admin, "acme", $"prof-{Guid.NewGuid():N}@acme.test");

        // Fresh user has no permission grants → even basic profile read is denied.
        var personal = await viewer.Http.GetAsync($"/api/v1/employees/{empId}/personal");
        personal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var stat = await viewer.Http.GetAsync($"/api/v1/employees/{empId}/statutory");
        stat.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
