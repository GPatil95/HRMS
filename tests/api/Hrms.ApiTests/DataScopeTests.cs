using System.Net;
using System.Text.Json;
using Hrms.Testing;
using P = Hrms.Application.Modules.FoundationPermissions;

namespace Hrms.ApiTests;

/// <summary>Data-scope filtering of the minimal employee directory for every scope level, via real role/override grants.</summary>
[Collection(HrmsCollection.Name)]
public sealed class DataScopeTests(HrmsFixture f)
{
    private sealed record Org(Guid B2, Guid M, Guid R1, Guid R2, Guid X, Guid S, Guid Y);

    private static string U(string p) => (p + Guid.NewGuid().ToString("N"))[..12];

    private static async Task<Org> BuildOrgAsync(HrmsClient admin)
    {
        Task<Guid> Company() => admin.PostForIdAsync("/api/v1/org/companies", new { code = U("C"), name = "Scope Co", legalName = (string?)null });
        Task<Guid> Branch(Guid c) => admin.PostForIdAsync("/api/v1/org/branches", new { companyId = c, code = U("B"), name = "Branch" });
        Task<Guid> Emp(Guid b, Guid? d, Guid? m) => admin.PostForIdAsync("/api/v1/employees", new { employeeCode = U("E"), displayName = "Emp", workEmail = (string?)null, branchId = b, departmentId = d, managerEmployeeId = m });

        var c1 = await Company();
        var c2 = await Company();
        var b1 = await Branch(c1);
        var b2 = await Branch(c1);
        var b3 = await Branch(c2);
        var d1 = await admin.PostForIdAsync("/api/v1/org/departments", new { code = U("D"), name = "Dept", branchId = b1 });
        var m = await Emp(b1, d1, null);
        var r1 = await Emp(b1, d1, m);
        var r2 = await Emp(b1, null, r1);
        return new Org(b2, m, r1, r2, await Emp(b2, null, null), await Emp(b2, null, null), await Emp(b3, null, null));
    }

    /// <summary>Creates a tenant user linked to <paramref name="self"/> holding employee.view at the given scope (no grant when scope is null).</summary>
    private async Task<HrmsClient> ScopedUserAsync(HrmsClient admin, Guid self, string? scope, string? targetKind = null, params Guid[] targets)
    {
        var (userId, client) = await f.CreateTenantUserAsync(admin, "acme", $"{U("scope")}@acme.test");
        // No user<->employee link API exists in M1 (minimal directory only), so the link is set directly in the tenant DB.
        var db = await f.TenantDatabaseAsync("acme");
        var schema = await f.ScalarAsync<string>(db, "select table_schema from information_schema.columns where table_name = 'users' and column_name = 'employee_id' limit 1");
        // users.employee_id is unique: move the link from any earlier test user to this one.
        await f.ScalarAsync<long>(db, $"with x as (update {schema}.users set employee_id = null where employee_id = @e returning 1) select count(*) from x", ("e", self));
        await f.ScalarAsync<long>(db, $"with x as (update {schema}.users set employee_id = @e where id = @u returning 1) select count(*) from x", ("e", self), ("u", userId));
        if (scope is not null)
        {
            await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new
            {
                permissionCode = P.EmployeeView,
                effect = "Grant",
                scopeLevel = scope,
                targetKind,
                targetIds = targets,
                reason = "data scope test",
                validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            });
        }

        return client;
    }

    private static async Task<HashSet<Guid>> VisibleAsync(HrmsClient c)
    {
        var r = await c.Http.GetAsync("/api/v1/employees?pageSize=200");
        r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToHashSet();
    }

    private static void Expect(HashSet<Guid> visible, Guid[] sees, Guid[] hidden)
    {
        foreach (var id in sees) visible.ShouldContain(id);
        foreach (var id in hidden) visible.ShouldNotContain(id);
    }

    [Fact]
    public async Task EveryScopeLevelFiltersTheDirectory()
    {
        using var admin = await f.AcmeAdminAsync();
        var o = await BuildOrgAsync(admin);

        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Own")), [o.M], [o.R1, o.R2, o.X, o.Y]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "DirectReports")), [o.M, o.R1], [o.R2, o.X]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "AllReports")), [o.M, o.R1, o.R2], [o.X, o.S]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Department")), [o.M, o.R1], [o.R2, o.X]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Branch")), [o.M, o.R1, o.R2], [o.X, o.S, o.Y]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Branch", "Branch", o.B2)), [o.X, o.S], [o.M, o.R1, o.Y]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Company")), [o.M, o.R1, o.R2, o.X, o.S], [o.Y]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "SelectedEmployees", "Employee", o.S)), [o.S], [o.M, o.X, o.Y]);
        Expect(await VisibleAsync(await ScopedUserAsync(admin, o.M, "Tenant")), [o.M, o.R1, o.R2, o.X, o.S, o.Y], []);
    }

    [Fact]
    public async Task OutOfScopeRecordIsNotReadableAndNoGrantIsForbidden()
    {
        using var admin = await f.AcmeAdminAsync();
        var o = await BuildOrgAsync(admin);

        using var own = await ScopedUserAsync(admin, o.R1, "DirectReports");
        (await own.Http.GetAsync($"/api/v1/employees/{o.R2}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.Http.GetAsync($"/api/v1/employees/{o.M}")).IsSuccessStatusCode.ShouldBeFalse();
        (await own.Http.GetAsync($"/api/v1/employees/{o.X}")).IsSuccessStatusCode.ShouldBeFalse();

        using var none = await ScopedUserAsync(admin, o.M, null);
        (await none.Http.GetAsync("/api/v1/employees")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
