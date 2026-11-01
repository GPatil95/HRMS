using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Employees;
using Hrms.Infrastructure.Persistence;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class EmployeeLinkTests(HrmsFixture f)
{
    private async Task<(HrmsClient admin, HrmsClient approver, Guid branchId)> SetupAsync()
    {
        var admin = await f.TenantLoginAsync("acme", "admin@acme.test");
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Link Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("AP"), name = "Approver", description = (string?)null,
            permissions = new[] { "employee.lifecycle.view", "employee.lifecycle.approve" }.Select(p => new { permissionCode = p, scopeLevel = "Tenant" }).ToArray(),
        });
        var (_, approver) = await f.CreateTenantUserAsync(admin, "acme", $"appr-{Guid.NewGuid():N}@acme.test", roleId);
        return (admin, approver, branchId);
    }

    private static Task<Guid> NewEmployeeAsync(HrmsClient admin, Guid branchId, string name, Guid? managerId = null) =>
        admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = name, workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = managerId });

    private static string Today(int days = 0) => DateOnly.FromDateTime(DateTime.Today.AddDays(days)).ToString("yyyy-MM-dd");

    private static async Task<Guid> ApproveAsync(HrmsClient admin, HrmsClient approver, Guid empId, string type, string effective, object? payload)
    {
        var create = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/lifecycle-events",
            new { eventType = type, effectiveDate = effective, reason = (string?)null, payload, submit = true });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var approve = await approver.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        return eventId;
    }

    private async Task SweepAsync(string database, int days)
    {
        await using var ctx = TenantDbContextFactory.Create(f.ServerCs(database));
        var lifecycle = new LifecycleEventService(ctx, new EmploymentHistoryService(ctx), new Hrms.Shared.Primitives.SystemClock());
        await lifecycle.ActivateDueEventsAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(days)), CancellationToken.None);
    }

    [Fact]
    public async Task LinkAndUnlinkUserKeepsBothSidesConsistent()
    {
        var (admin, _, branchId) = await SetupAsync();
        var empId = await NewEmployeeAsync(admin, branchId, "Linked Emp");
        var empId2 = await NewEmployeeAsync(admin, branchId, "Other Emp");

        // Create a tenant user, then link employee -> user.
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("R"), name = "Plain", description = (string?)null, permissions = Array.Empty<object>(),
        });
        var (userId, _) = await f.CreateTenantUserAsync(admin, "acme", $"u-{Guid.NewGuid():N}@acme.test", roleId);

        var link = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId}/user", new { userId });
        link.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var emp = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        emp.GetProperty("userId").GetGuid().ShouldBe(userId);
        var user = await admin.GetJsonAsync($"/api/v1/users/{userId}");
        user.GetProperty("user").GetProperty("employeeId").GetGuid().ShouldBe(empId);

        // The same user cannot be linked to another employee.
        var dup = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{empId2}/user", new { userId });
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Unlink clears both sides.
        var unlink = await admin.Http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/employees/{empId}/user"));
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync($"/api/v1/employees/{empId}")).GetProperty("userId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await admin.GetJsonAsync($"/api/v1/users/{userId}")).GetProperty("user").GetProperty("employeeId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ManagerChangeRebuildsReportingClosure()
    {
        var (admin, approver, branchId) = await SetupAsync();
        var ceo = await NewEmployeeAsync(admin, branchId, "CEO");
        var m1 = await NewEmployeeAsync(admin, branchId, "Manager One", ceo);
        var m2 = await NewEmployeeAsync(admin, branchId, "Manager Two", ceo);
        var e1 = await NewEmployeeAsync(admin, branchId, "Employee", m1);
        var child = await NewEmployeeAsync(admin, branchId, "Child", e1);

        var db = await f.TenantDatabaseAsync("acme");
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @m AND descendant_id = @e AND depth = 1",
            ("m", m1), ("e", e1))).ShouldBe(1);

        // Future-dated so the approve lands cleanly; the daily sweep then applies it.
        await ApproveAsync(admin, approver, e1, "manager_change", Today(1), new { managerEmployeeId = m2 });
        await SweepAsync(db, 1);

        // E1 (and its whole subtree) moved under M2; the M1 ancestor links are gone.
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @m AND descendant_id = @e AND depth = 1",
            ("m", m2), ("e", e1))).ShouldBe(1);
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @m AND descendant_id = @e",
            ("m", m1), ("e", e1))).ShouldBe(0);
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @m AND descendant_id = @c AND depth = 2",
            ("m", m2), ("c", child))).ShouldBe(1);
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @e AND descendant_id = @c AND depth = 1",
            ("e", e1), ("c", child))).ShouldBe(1); // intra-subtree link preserved
        (await f.ScalarAsync<int>(db,
            "SELECT count(*) FROM org.reporting_closure WHERE ancestor_id = @c AND descendant_id = @c2 AND depth = 3",
            ("c", ceo), ("c2", child))).ShouldBe(1); // ceo -> m2 -> e1 -> child
    }

    [Fact]
    public async Task ManagerChangeToDescendantIsRejected()
    {
        var (admin, approver, branchId) = await SetupAsync();
        var ceo = await NewEmployeeAsync(admin, branchId, "CEO");
        var e1 = await NewEmployeeAsync(admin, branchId, "Employee", ceo);

        var eventId = await ApproveAsync(admin, approver, ceo, "manager_change", Today(1), new { managerEmployeeId = e1 });

        // The sweep hits the cycle: apply fails and the event reverts to submitted, never effective.
        var db = await f.TenantDatabaseAsync("acme");
        await SweepAsync(db, 1);
        var ev = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}");
        ev.GetProperty("status").GetString().ShouldBe("submitted");
    }
}
