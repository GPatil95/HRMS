using System.Net;
using System.Net.Http.Json;
using Hrms.Application.Employees;
using Hrms.Domain.Tenant;
using Hrms.Infrastructure.Persistence;
using Hrms.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class HrCoreSchemaTests(HrmsFixture f)
{
    private async Task<TenantDbContext> TenantAsync(string tenant = "acme")
    {
        var db = await f.TenantDatabaseAsync(tenant);
        return TenantDbContextFactory.Create(f.ServerCs(db));
    }

    private static Employee NewEmployee(Guid companyId, Guid branchId, string code) => new()
    {
        EmployeeCode = code, DisplayName = "Test Emp", CompanyId = companyId, BranchId = branchId,
    };

    private static EmploymentHistory OpenRow(Guid employeeId, Guid companyId, Guid branchId, DateOnly from, string evt = LifecycleEventTypes.InitialAssignment) => new()
    {
        EmployeeId = employeeId, CompanyId = companyId, BranchId = branchId, EventType = evt,
        EffectiveFrom = from, EmploymentType = EmploymentTypes.Permanent,
    };

    [Fact]
    public async Task CodeSeriesIncrementsAtomically()
    {
        await using var db = await TenantAsync("globex");
        var svc = new EmployeeCodeService(db);

        var c1 = await svc.NextCodeAsync("TST", 4, default);
        var c2 = await svc.NextCodeAsync("TST", 4, default);
        var c3 = await svc.NextCodeAsync("TST", 4, default);

        c1.ShouldBe("TST0001");
        c2.ShouldBe("TST0002");
        c3.ShouldBe("TST0003");

        // A second series key shares nothing.
        (await svc.NextCodeAsync("OTH", 3, default)).ShouldBe("OTH001");
    }

    [Fact]
    public async Task ManualCodeRejectsDuplicatesAndHonoursAllowManual()
    {
        await using var db = await TenantAsync();
        var company = new Company { Code = TestData.Unique("C"), Name = "Co" };
        var branch = new Branch { CompanyId = company.Id, Code = TestData.Unique("B"), Name = "HQ", TimeZone = "UTC" };
        db.Companies.Add(company);
        db.Branches.Add(branch);
        var emp = NewEmployee(company.Id, branch.Id, "MAN001");
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var svc = new EmployeeCodeService(db);
        var cfg = new EmployeeNumberingConfig { Prefix = "EMP", AllowManual = true };

        var dup = await svc.ValidateManualCodeAsync(cfg, "MAN001", default);
        dup.IsSuccess.ShouldBeFalse();
        dup.Error!.Code.ShouldBe("code-taken");

        var noManual = await svc.ValidateManualCodeAsync(new EmployeeNumberingConfig { AllowManual = false }, "NEW1", default);
        noManual.IsSuccess.ShouldBeFalse();
        noManual.Error!.Code.ShouldBe("manual-code-disabled");

        (await svc.ValidateManualCodeAsync(cfg, "FREE01", default)).Value.ShouldBe("FREE01");
    }

    [Fact]
    public async Task HistoryTransitionClosesOpenRowAndReprojects()
    {
        await using var db = await TenantAsync();
        var company = new Company { Code = TestData.Unique("C"), Name = "Co" };
        var branchA = new Branch { CompanyId = company.Id, Code = TestData.Unique("B"), Name = "A", TimeZone = "UTC" };
        var branchB = new Branch { CompanyId = company.Id, Code = TestData.Unique("B"), Name = "B", TimeZone = "UTC" };
        db.Companies.Add(company);
        db.Branches.AddRange(branchA, branchB);
        var emp = NewEmployee(company.Id, branchA.Id, TestData.Unique("E"));
        db.Employees.Add(emp);
        db.EmploymentHistory.Add(OpenRow(emp.Id, company.Id, branchA.Id, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        var svc = new EmploymentHistoryService(db);
        var next = OpenRow(emp.Id, company.Id, branchB.Id, new DateOnly(2025, 6, 1), LifecycleEventTypes.Transfer);
        var r = await svc.ApplyTransitionAsync(emp, next, default);
        r.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var rows = await svc.ListAsync(emp.Id, default);
        rows.Count.ShouldBe(2);
        rows[0].EffectiveTo.ShouldBeNull();
        rows[0].BranchId.ShouldBe(branchB.Id);
        rows[1].EffectiveTo.ShouldBe(new DateOnly(2025, 6, 1));

        emp.BranchId.ShouldBe(branchB.Id); // projection follows the open row

        // As-of queries see the right period.
        (await svc.AsOfAsync(emp.Id, new DateOnly(2025, 3, 1), default))!.BranchId.ShouldBe(branchA.Id);
        (await svc.AsOfAsync(emp.Id, new DateOnly(2025, 7, 1), default))!.BranchId.ShouldBe(branchB.Id);
    }

    [Fact]
    public async Task BackdatedTransitionIsRejected()
    {
        await using var db = await TenantAsync();
        var company = new Company { Code = TestData.Unique("C"), Name = "Co" };
        var branch = new Branch { CompanyId = company.Id, Code = TestData.Unique("B"), Name = "HQ", TimeZone = "UTC" };
        db.Companies.Add(company);
        db.Branches.Add(branch);
        var emp = NewEmployee(company.Id, branch.Id, TestData.Unique("E"));
        db.Employees.Add(emp);
        db.EmploymentHistory.Add(OpenRow(emp.Id, company.Id, branch.Id, new DateOnly(2025, 3, 1)));
        await db.SaveChangesAsync();

        var svc = new EmploymentHistoryService(db);
        var backdated = OpenRow(emp.Id, company.Id, branch.Id, new DateOnly(2025, 1, 1), LifecycleEventTypes.Transfer);
        var r = await svc.ApplyTransitionAsync(emp, backdated, default);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("history-overlap");
    }

    [Fact]
    public async Task ExclusionConstraintBlocksSecondOpenRow()
    {
        await using var db = await TenantAsync();
        var company = new Company { Code = TestData.Unique("C"), Name = "Co" };
        var branch = new Branch { CompanyId = company.Id, Code = TestData.Unique("B"), Name = "HQ", TimeZone = "UTC" };
        db.Companies.Add(company);
        db.Branches.Add(branch);
        var emp = NewEmployee(company.Id, branch.Id, TestData.Unique("E"));
        db.Employees.Add(emp);
        db.EmploymentHistory.Add(OpenRow(emp.Id, company.Id, branch.Id, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        // Second overlapping open row → the EXCLUDE constraint must reject it at the DB level.
        db.EmploymentHistory.Add(OpenRow(emp.Id, company.Id, branch.Id, new DateOnly(2025, 2, 1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task HistoryInvariantsHoldAndNumberingConfigSeeded()
    {
        // At most one open (effective_to IS NULL) history row per employee, and no overlapping periods.
        // (Employees created via the directory API get their open row when M2.2 wires creation through history.)
        var db = await f.TenantDatabaseAsync("acme");
        var multiOpen = await f.ScalarAsync<long>(db,
            "select count(*) from (select employee_id from org.employment_history where effective_to is null group by employee_id having count(*) > 1) x");
        multiOpen.ShouldBe(0);

        var overlap = await f.ScalarAsync<long>(db,
            """
            select count(*) from org.employment_history a join org.employment_history b
              on a.employee_id = b.employee_id and a.id <> b.id
             and daterange(a.effective_from, coalesce(a.effective_to, 'infinity'::date), '[)') &&
                 daterange(b.effective_from, coalesce(b.effective_to, 'infinity'::date), '[)')
            """);
        overlap.ShouldBe(0);

        var cfg = await f.ScalarAsync<long>(db, "select count(*) from org.employee_numbering_config where company_id is null");
        cfg.ShouldBe(1);
    }
}
