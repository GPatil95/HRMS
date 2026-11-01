using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Employees;
using Hrms.Infrastructure.Persistence;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class EmployeeSalaryTests(HrmsFixture f)
{
    private static string Today(int days = 0) => DateOnly.FromDateTime(DateTime.Today.AddDays(days)).ToString("yyyy-MM-dd");

    private async Task<Guid> ComponentIdAsync(HrmsClient admin, string code)
    {
        var list = await admin.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/salary/components");
        return list!.First(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private async Task<Guid> MakeStructureAsync(HrmsClient admin, string code, decimal basicAmount = 40000m, decimal hraPercent = 40m)
    {
        var basicId = await ComponentIdAsync(admin, "basic");
        var hraId = await ComponentIdAsync(admin, "hra");
        var res = await admin.Http.PostAsJsonAsync("/api/v1/salary/structures", new
        {
            code, name = $"Structure {code}", description = (string?)null, isActive = true,
            lines = new object[]
            {
                new { salaryComponentId = basicId, sequence = 1, calcType = "fixed", amount = basicAmount, percent = (decimal?)null, formula = (string?)null },
                new { salaryComponentId = hraId, sequence = 2, calcType = "percent_of_basic", amount = (decimal?)null, percent = hraPercent, formula = (string?)null },
            },
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> NewEmployeeAsync(HrmsClient admin)
    {
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Sal Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        return await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "Salaried Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
    }

    private async Task<Guid> CreateAssignmentAsync(HrmsClient admin, Guid empId, Guid structureId, decimal ctc, string effective, bool submit)
    {
        var res = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/salary-assignments",
            new { salaryStructureId = structureId, ctcAmount = ctc, currency = (string?)null, effectiveFrom = effective, reason = (string?)null, submit });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task SeededComponentsAndCrudWork()
    {
        var admin = await f.AcmeAdminAsync();
        var list = await admin.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/salary/components");
        list!.Length.ShouldBeGreaterThanOrEqualTo(15);
        list.Any(c => c.GetProperty("code").GetString() == "basic" && c.GetProperty("isBasic").GetBoolean()).ShouldBeTrue();

        var res = await admin.Http.PostAsJsonAsync("/api/v1/salary/components", new
        {
            code = "shift-allowance", name = "Shift Allowance", type = "earning", payslipName = (string?)null,
            taxable = true, proRata = true, partOfCtc = true, partOfGross = true, isBasic = false, displayOrder = 70, isActive = true,
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var del = await admin.Http.DeleteAsync($"/api/v1/salary/components/{id}");
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Statutory components are delete-protected.
        var pfId = await ComponentIdAsync(admin, "pf-employee");
        (await admin.Http.DeleteAsync($"/api/v1/salary/components/{pfId}")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task StructureValidationRejectsBadLines()
    {
        var admin = await f.AcmeAdminAsync();
        var basicId = await ComponentIdAsync(admin, "basic");
        var res = await admin.Http.PostAsJsonAsync("/api/v1/salary/structures", new
        {
            code = TestData.Unique("S"), name = "Bad", description = (string?)null, isActive = true,
            lines = new object[]
            {
                new { salaryComponentId = basicId, sequence = 1, calcType = "percent_of_basic", amount = (decimal?)null, percent = (decimal?)null, formula = (string?)null },
            },
        });
        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignmentLifecycleActivatesAndResolvesLines()
    {
        var admin = await f.AcmeAdminAsync();
        var approver = admin; // tenant admin has all permissions but self-approval is blocked → use a second user
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("SA"), name = "Salary Approver", description = (string?)null,
            permissions = new[] { "salary.assignment.view", "salary.assignment.approve" }.Select(p => new { permissionCode = p, scopeLevel = "Tenant" }).ToArray(),
        });
        var (_, approverClient) = await f.CreateTenantUserAsync(admin, "acme", $"sa-{Guid.NewGuid():N}@acme.test", roleId);
        approver = approverClient;

        var empId = await NewEmployeeAsync(admin);
        var structureId = await MakeStructureAsync(admin, TestData.Unique("S"));

        // Past-dated approved assignment activates inline on approval.
        var a1 = await CreateAssignmentAsync(admin, empId, structureId, 900000m, Today(-30), submit: true);
        (await admin.Http.PostAsync($"/api/v1/employees/salary-assignments/{a1}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden); // self-approval
        (await approver.Http.PostAsync($"/api/v1/employees/salary-assignments/{a1}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var current = await admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{empId}/salary-assignments/current");
        current.GetProperty("status").GetString().ShouldBe("active");
        current.GetProperty("ctcAmount").GetDecimal().ShouldBe(900000m);
        var lines = current.GetProperty("lines").EnumerateArray().OrderBy(l => l.GetProperty("sequence").GetInt32()).ToList();
        lines[0].GetProperty("resolvedAmount").GetDecimal().ShouldBe(40000m);
        lines[1].GetProperty("resolvedAmount").GetDecimal().ShouldBe(16000m); // 40% of basic

        // A second assignment can't be created while a pending one exists.
        var a2 = await CreateAssignmentAsync(admin, empId, structureId, 1000000m, Today(30), submit: true);
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/salary-assignments",
            new { salaryStructureId = structureId, ctcAmount = 1m, currency = (string?)null, effectiveFrom = Today(60), reason = (string?)null, submit = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await approver.Http.PostAsync($"/api/v1/employees/salary-assignments/{a2}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Sweep at day+30 activates the second and closes the first.
        var db = await f.TenantDatabaseAsync("acme");
        await using var ctx = TenantDbContextFactory.Create(f.ServerCs(db));
        var salary = new SalaryService(ctx, new Hrms.Shared.Primitives.SystemClock());
        await salary.ActivateDueAssignmentsAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(30)), CancellationToken.None);

        var cur2 = await admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{empId}/salary-assignments/current");
        cur2.GetProperty("ctcAmount").GetDecimal().ShouldBe(1000000m);

        var history = (await admin.Http.GetFromJsonAsync<JsonElement[]>($"/api/v1/employees/{empId}/salary-assignments"))!
            .ToList();
        history.Single(a => a.GetProperty("id").GetGuid() == a1).GetProperty("status").GetString().ShouldBe("closed");
        history.Single(a => a.GetProperty("id").GetGuid() == a1).GetProperty("effectiveTo").GetString().ShouldBe(Today(30));

        // as-of read returns the right period row (payroll-readiness).
        var asOf = await admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{empId}/salary-assignments/as-of?date={Today()}");
        asOf.GetProperty("id").GetGuid().ShouldBe(a1);
    }

    [Fact]
    public async Task RejectAndCancelWork()
    {
        var admin = await f.AcmeAdminAsync();
        var empId = await NewEmployeeAsync(admin);
        var structureId = await MakeStructureAsync(admin, TestData.Unique("S"));

        var draft = await CreateAssignmentAsync(admin, empId, structureId, 500000m, Today(10), submit: false);
        (await admin.Http.PostAsync($"/api/v1/employees/salary-assignments/{draft}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Admin (same user) can reject — reject is not SoD-blocked.
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/salary-assignments/{draft}/reject", new { reason = "nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var cancel = await CreateAssignmentAsync(admin, empId, structureId, 500000m, Today(10), submit: false);
        (await admin.Http.PostAsync($"/api/v1/employees/salary-assignments/{cancel}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
