using System.Net;
using System.Reflection;
using Hrms.Api.Security;
using Hrms.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class ReportIsolationTests(HrmsFixture f)
{
    private static async Task<string> CreateEmployeeWithCodeAsync(HrmsClient admin)
    {
        var company = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Company", legalName = (string?)null });
        var branch = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = company, code = TestData.Unique("B"), name = "Branch" });
        var code = TestData.Unique("RPT");
        await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = code, displayName = "Report Emp", workEmail = (string?)null, branchId = branch, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        return code;
    }

    [Fact]
    public async Task ReportsAndExportsOnlyContainCurrentTenantData()
    {
        using var acme = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();
        var code = await CreateEmployeeWithCodeAsync(acme);

        (await acme.Http.GetStringAsync("/api/v1/reports/employee-directory")).ShouldContain(code);
        (await globex.Http.GetStringAsync($"/api/v1/reports/employee-directory?tenantId={f.AcmeId}")).ShouldNotContain(code);

        var acmeExport = await acme.Http.GetAsync("/api/v1/reports/employee-directory/export");
        acmeExport.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await acmeExport.Content.ReadAsByteArrayAsync()).Length.ShouldBeGreaterThan(0);
        var globexExport = await globex.Http.GetAsync("/api/v1/reports/employee-directory/export");
        globexExport.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadExportTextAsync(globexExport)).ShouldNotContain(code);
        (await ReadExportTextAsync(acmeExport)).ShouldContain(code);
    }

    [Fact]
    public async Task ExportRequiresExportPermission()
    {
        using var admin = await f.AcmeAdminAsync();
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", $"rpt-{Guid.NewGuid():N}@acme.test");
        using (user)
        {
            await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new { permissionCode = Hrms.Application.Modules.FoundationPermissions.ReportView, effect = "Grant", scopeLevel = "Tenant", reason = "test", validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });
            (await user.Http.GetAsync("/api/v1/reports")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await user.Http.GetAsync("/api/v1/reports/employee-directory/export")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>Exports may be CSV or XLSX; XLSX shared strings are extracted from the zip.</summary>
    private static async Task<string> ReadExportTextAsync(HttpResponseMessage r)
    {
        var bytes = await r.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
            var sb = new System.Text.StringBuilder();
            foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                using var sr = new StreamReader(e.Open());
                sb.Append(await sr.ReadToEndAsync());
            }

            return sb.ToString();
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}

public sealed class ApiSurfaceArchitectureTests
{
    private static readonly Type[] Guards = [typeof(AllowAnonymousAttribute), typeof(AuthenticatedAttribute), typeof(RequirePermissionAttribute), typeof(RequirePlatformPermissionAttribute)];

    [Fact]
    public void EveryEndpointDeclaresAnAuthorizationGuard()
    {
        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => (t, m)))
            .Where(x => x.m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
            .Where(x => !Guards.Any(g => x.m.IsDefined(g, true) || x.t.IsDefined(g, true)))
            .Select(x => $"{x.t.Name}.{x.m.Name}")
            .ToList();
        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }
}
