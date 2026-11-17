using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Employees;
using Hrms.Infrastructure.Persistence;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class LifecycleEventTests(HrmsFixture f)
{
    private const string ApproveRolePerms = "employee.lifecycle.view,employee.lifecycle.approve";

    private async Task<(HrmsClient admin, HrmsClient approver, Guid empId)> SetupAsync(bool preJoining = false)
    {
        var admin = await f.TenantLoginAsync("acme", "admin@acme.test");
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "LC Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "LC Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null, preJoining });

        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("AP"), name = "Lifecycle approver", description = (string?)null,
            permissions = ApproveRolePerms.Split(',').Select(p => new { permissionCode = p, scopeLevel = "Tenant" }).ToArray(),
        });
        var (_, approver) = await f.CreateTenantUserAsync(admin, "acme", $"appr-{Guid.NewGuid():N}@acme.test", roleId);
        return (admin, approver, empId);
    }

    private static JsonContent EventBody(string type, string effective, object? payload = null, string? reason = null, bool submit = true) =>
        JsonContent.Create(new { eventType = type, effectiveDate = effective, reason, payload, submit });

    private static string Today(int days = 0) => DateOnly.FromDateTime(DateTime.Today.AddDays(days)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task JoiningEventActivatesPreJoiningEmployee()
    {
        var (admin, approver, empId) = await SetupAsync(preJoining: true);
        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("joining", Today(), new { joiningDate = Today(), employmentType = "permanent" }));
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await approver.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var emp = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        emp.GetProperty("status").GetString().ShouldBe("Active");

        var ev = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}");
        ev.GetProperty("status").GetString().ShouldBe("effective");

        var history = await admin.GetJsonAsync($"/api/v1/employees/{empId}/history");
        var rows = history.EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows.Single(r => r.GetProperty("effectiveTo").ValueKind == JsonValueKind.Null)
            .GetProperty("eventType").GetString().ShouldBe("joining");
    }

    [Fact]
    public async Task SubmitterCannotApproveOwnEvent()
    {
        var (admin, _, empId) = await SetupAsync(preJoining: true);
        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events", EventBody("joining", Today()));
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Admin submitted it, so admin cannot approve it.
        var self = await admin.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FutureDatedEventAppliesOnActivationSweep()
    {
        var (admin, approver, empId) = await SetupAsync();
        var future = Today(10);
        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("manager_change", future, new { managerEmployeeId = (Guid?)null }));
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await approver.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var ev = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}");
        ev.GetProperty("status").GetString().ShouldBe("approved"); // future-dated: not yet effective

        // Run the activation sweep directly against the tenant DB as of the event's date.
        var db = await f.TenantDatabaseAsync("acme");
        var cs = f.ServerCs(db);
        await using var ctx = TenantDbContextFactory.Create(cs);
        var lifecycle = new LifecycleEventService(ctx, new EmploymentHistoryService(ctx), new Hrms.Shared.Primitives.SystemClock());
        var applied = await lifecycle.ActivateDueEventsAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(10)), CancellationToken.None);
        applied.ShouldBeGreaterThanOrEqualTo(1);

        var after = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}");
        after.GetProperty("status").GetString().ShouldBe("effective");
    }

    [Fact]
    public async Task ExitEventClosesHistoryAndMarksEmployee()
    {
        var (admin, approver, empId) = await SetupAsync();
        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("exit", Today(1), new { lastWorkingDate = Today(1), exitReason = "resignation" }));
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await approver.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var db = await f.TenantDatabaseAsync("acme");
        await using var ctx = TenantDbContextFactory.Create(f.ServerCs(db));
        var lifecycle = new LifecycleEventService(ctx, new EmploymentHistoryService(ctx), new Hrms.Shared.Primitives.SystemClock());
        await lifecycle.ActivateDueEventsAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(1)), CancellationToken.None);

        var emp = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        emp.GetProperty("status").GetString().ShouldBe("Exited");

        var history = await admin.GetJsonAsync($"/api/v1/employees/{empId}/history");
        history.EnumerateArray().All(r => r.GetProperty("effectiveTo").ValueKind != JsonValueKind.Null).ShouldBeTrue();
    }

    [Fact]
    public async Task RejectAndCancelFollowRules()
    {
        var (admin, approver, empId) = await SetupAsync();

        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("confirmation", Today(30), new { confirmationDate = Today(30) }));
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var reject = await approver.Http.PostAsJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}/reject", new { reason = "probation not done" });
        reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ev = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{eventId}");
        ev.GetProperty("status").GetString().ShouldBe("rejected");

        var create2 = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("transfer", Today(20), submit: false));
        var event2 = (await create2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.Http.PostAsync($"/api/v1/employees/lifecycle-events/{event2}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var ev2 = await admin.GetJsonAsync($"/api/v1/employees/lifecycle-events/{event2}");
        ev2.GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Fact]
    public async Task TransferEventReprojectsEmployee()
    {
        var (admin, approver, empId) = await SetupAsync();
        var emp = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        var companyId = emp.GetProperty("companyId").GetGuid();
        var newBranch = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "Branch2" });

        var create = await admin.Http.PostAsync($"/api/v1/employees/{empId}/lifecycle-events",
            EventBody("transfer", Today(1), new { branchId = newBranch }));
        var eventId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await approver.Http.PostAsync($"/api/v1/employees/lifecycle-events/{eventId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var db = await f.TenantDatabaseAsync("acme");
        await using var ctx = TenantDbContextFactory.Create(f.ServerCs(db));
        var lifecycle = new LifecycleEventService(ctx, new EmploymentHistoryService(ctx), new Hrms.Shared.Primitives.SystemClock());
        await lifecycle.ActivateDueEventsAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(1)), CancellationToken.None);

        var after = await admin.GetJsonAsync($"/api/v1/employees/{empId}");
        after.GetProperty("branchId").GetGuid().ShouldBe(newBranch);
    }
}
