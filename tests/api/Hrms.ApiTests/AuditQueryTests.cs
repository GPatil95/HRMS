using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class AuditQueryTests(HrmsFixture f)
{
    private static async Task<JsonElement[]> EntriesAsync(HrmsClient c, string qs) =>
        (await c.Http.GetFromJsonAsync<JsonElement[]>($"/api/v1/audit/entries?{qs}"))!;

    [Fact]
    public async Task CreateUpdateAndDeleteFieldsWithLabels()
    {
        var admin = await f.AcmeAdminAsync();
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Audit Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "Audit Branch" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "Audit Target", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });

        var entries = await EntriesAsync(admin, $"entityType=Employee&entityId={empId}");
        var create = entries.Single(e => e.GetProperty("action").GetString() == "create");
        create.GetProperty("moduleCode").GetString().ShouldBe("core.employee");
        create.GetProperty("entityLabel").GetString().ShouldBe("Audit Target");
        create.GetProperty("actorDisplay").GetString().ShouldNotBeNullOrEmpty();

        var fields = create.GetProperty("fields").EnumerateArray().ToDictionary(x => x.GetProperty("field").GetString()!);
        fields["DisplayName"].GetProperty("newValue").GetString().ShouldBe("Audit Target");
        fields["DisplayName"].GetProperty("oldValue").ValueKind.ShouldBe(JsonValueKind.Null);
        // FK label resolution: BranchId shows the branch name, not just the guid.
        fields["BranchId"].GetProperty("newLabel").GetString().ShouldBe("Audit Branch");

        // First PUT creates the section row (INSERT: old NULL, new set)…
        var body = new
        {
            dateOfBirth = "1990-01-01", gender = "male", maritalStatus = (string?)null, nationality = (string?)null,
            bloodGroup = (string?)null, personalEmail = (string?)null, personalPhone = (string?)null,
            currentAddress = (object?)null, permanentAddress = (object?)null, rowVersion = (uint?)null,
        };
        (await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/personal", body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        bool IsMine(JsonElement e) => e.GetProperty("fields").EnumerateArray()
            .Any(x => x.GetProperty("field").GetString() == "EmployeeId" &&
                      x.GetProperty("newValue").GetString() == empId.ToString());

        var createdEntry = (await EntriesAsync(admin, "entityType=EmployeePersonal&action=create")).Single(IsMine);
        var cf = createdEntry.GetProperty("fields").EnumerateArray().ToDictionary(x => x.GetProperty("field").GetString()!);
        cf["DateOfBirth"].GetProperty("oldValue").ValueKind.ShouldBe(JsonValueKind.Null);
        cf["DateOfBirth"].GetProperty("newValue").GetString()!.ShouldContain("1990");

        // …second PUT is the UPDATE: only touched fields, with old and new.
        (await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/personal", body with { dateOfBirth = "1991-05-05" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var sectionId = createdEntry.GetProperty("entityId").GetString();
        var update = (await EntriesAsync(admin, $"entityType=EmployeePersonal&action=update&entityId={sectionId}")).Single();
        var uf = update.GetProperty("fields").EnumerateArray().ToList();
        var dob = uf.Single(x => x.GetProperty("field").GetString() == "DateOfBirth");
        dob.GetProperty("oldValue").GetString()!.ShouldContain("1990");
        dob.GetProperty("newValue").GetString()!.ShouldContain("1991");
    }

    [Fact]
    public async Task MaskedFieldsNeverExposeValues()
    {
        var admin = await f.AcmeAdminAsync();
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Mask Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "Masked Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });

        var put = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/statutory", new
        {
            pan = "ABCDE1234F", uan = (string?)null, pfNumber = (string?)null, pfApplicable = false,
            esiNumber = (string?)null, esiApplicable = false, ptState = (string?)null, lwfApplicable = false,
            aadhaarLast4 = (string?)null, passport = (string?)null, taxRegime = "new_regime",
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var entries = await EntriesAsync(admin, "entityType=EmployeeStatutory");
        var body = JsonSerializer.Serialize(entries);
        body.ShouldNotContain("ABCDE1234F");
        var mine = entries.Single(e => e.GetProperty("fields").EnumerateArray()
            .Any(x => x.GetProperty("field").GetString() == "EmployeeId" &&
                      x.GetProperty("newValue").GetString() == empId.ToString()));
        var pan = mine.GetProperty("fields").EnumerateArray()
            .Single(x => x.GetProperty("field").GetString() == "PanEncrypted");
        pan.GetProperty("masked").GetBoolean().ShouldBeTrue();
        pan.GetProperty("newValue").GetString().ShouldBe("***");
    }

    [Fact]
    public async Task AuditViewRequiresPermission()
    {
        var admin = await f.AcmeAdminAsync();
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("NP"), name = "No Audit", description = (string?)null, permissions = Array.Empty<object>(),
        });
        var (_, plain) = await f.CreateTenantUserAsync(admin, "acme", $"na-{Guid.NewGuid():N}@acme.test", roleId);
        (await plain.Http.GetAsync("/api/v1/audit/entries")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
