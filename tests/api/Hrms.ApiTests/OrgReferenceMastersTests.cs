using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class OrgReferenceMastersTests(HrmsFixture f)
{
    [Fact]
    public async Task DesignationCrudRoundTrips()
    {
        using var admin = await f.AcmeAdminAsync();
        var code = TestData.Unique("D");

        var id = await admin.PostForIdAsync("/api/v1/org/designations", new { code, name = "Senior Engineer", level = 3, description = "IC-3" });
        var list = await admin.GetJsonAsync("/api/v1/org/designations?includeInactive=true");
        var row = list.EnumerateArray().First(x => x.GetProperty("id").GetGuid() == id);
        row.GetProperty("level").GetInt32().ShouldBe(3);
        row.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        row.GetProperty("rowVersion").GetUInt32().ShouldBeGreaterThan(0u);

        var rv = row.GetProperty("rowVersion").GetUInt32();
        var upd = await admin.Http.PutAsJsonAsync($"/api/v1/org/designations/{id}", new { code, name = "Sr Engineer II", level = 4, description = (string?)null, rowVersion = rv });
        upd.IsSuccessStatusCode.ShouldBeTrue(await upd.Content.ReadAsStringAsync());

        var stale = await admin.Http.PutAsJsonAsync($"/api/v1/org/designations/{id}", new { code, name = "Stale", level = (int?)null, description = (string?)null, rowVersion = rv });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var off = await admin.Http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/org/designations/{id}/status") { Content = JsonContent.Create(new { isActive = false }) });
        off.IsSuccessStatusCode.ShouldBeTrue(await off.Content.ReadAsStringAsync());
        var activeOnly = await admin.GetJsonAsync("/api/v1/org/designations");
        activeOnly.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == id).ShouldBeFalse();

        var del = await admin.Http.DeleteAsync($"/api/v1/org/designations/{id}");
        del.IsSuccessStatusCode.ShouldBeTrue(await del.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DuplicateMasterCodeConflictsAcrossKinds()
    {
        using var admin = await f.AcmeAdminAsync();
        var code = TestData.Unique("X");

        await admin.PostForIdAsync("/api/v1/org/grades", new { code, name = "G", sortOrder = 1, description = (string?)null });
        var dup = await admin.Http.PostAsJsonAsync("/api/v1/org/grades", new { code, name = "Other", sortOrder = 2, description = (string?)null });
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Same code is fine on a different master kind.
        await admin.PostForIdAsync("/api/v1/org/cost-centres", new { code, name = "CC", companyId = (Guid?)null, description = (string?)null });
    }

    [Fact]
    public async Task LocationAndCostCentreValidateParents()
    {
        using var admin = await f.AcmeAdminAsync();

        var badBranch = await admin.Http.PostAsJsonAsync("/api/v1/org/locations",
            new { code = TestData.Unique("L"), name = "Loc", branchId = Guid.NewGuid(), locationType = "office", description = (string?)null });
        badBranch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badType = await admin.Http.PostAsJsonAsync("/api/v1/org/locations",
            new { code = TestData.Unique("L"), name = "Loc", branchId = (Guid?)null, locationType = "nonsense", description = (string?)null });
        badType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badCompany = await admin.Http.PostAsJsonAsync("/api/v1/org/cost-centres",
            new { code = TestData.Unique("K"), name = "CC", companyId = Guid.NewGuid(), description = (string?)null });
        badCompany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var branch = await admin.PostForIdAsync("/api/v1/org/branches",
            new { companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Co", legalName = (string?)null }), code = TestData.Unique("B"), name = "HQ" });
        var loc = await admin.PostForIdAsync("/api/v1/org/locations",
            new { code = TestData.Unique("L"), name = "Floor 2", branchId = branch, locationType = "floor", description = (string?)null });
        var list = await admin.GetJsonAsync("/api/v1/org/locations");
        list.EnumerateArray().First(x => x.GetProperty("id").GetGuid() == loc).GetProperty("branchName").GetString().ShouldBe("HQ");
    }

    [Fact]
    public async Task MastersRequirePermissions()
    {
        using var admin = await f.AcmeAdminAsync();
        var (_, viewer) = await f.CreateTenantUserAsync(admin, "acme", $"mast-{Guid.NewGuid():N}@acme.test");
        using (viewer)
        {
            var create = await viewer.Http.PostAsJsonAsync("/api/v1/org/designations", new { code = TestData.Unique("D"), name = "X", level = (int?)null, description = (string?)null });
            create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task MastersAreTenantIsolated()
    {
        using var admin = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();

        var code = TestData.Unique("D");
        var id = await admin.PostForIdAsync("/api/v1/org/designations", new { code, name = "Acme Only", level = (int?)null, description = (string?)null });

        var cross = await globex.Http.GetAsync("/api/v1/org/designations");
        var rows = JsonDocument.Parse(await cross.Content.ReadAsStringAsync()).RootElement;
        rows.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == id).ShouldBeFalse();

        // Same code is usable in the other tenant (isolation, not global uniqueness).
        await globex.PostForIdAsync("/api/v1/org/designations", new { code, name = "Globex Copy", level = (int?)null, description = (string?)null });
    }
}
