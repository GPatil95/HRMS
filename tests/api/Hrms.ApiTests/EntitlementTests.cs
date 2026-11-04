using System.Net;
using System.Net.Http.Json;
using Hrms.Application.Modules;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class EntitlementTests(HrmsFixture f)
{
    private static Task<HttpResponseMessage> SetModuleAsync(HrmsClient platform, Guid tenantId, string module, bool enabled) =>
        platform.Http.PutAsJsonAsync($"/api/v1/platform/tenants/{tenantId}/modules", new { moduleCode = module, enabled, reason = "entitlement test", validTo = (DateOnly?)null });

    [Fact]
    public async Task DisabledModuleDeniesAccessEvenWhenPermissionIsHeld()
    {
        using var platform = await f.PlatformAdminAsync();
        using var admin = await f.GlobexAdminAsync();
        (await admin.Http.GetAsync("/api/v1/probe/payroll")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var on = await SetModuleAsync(platform, f.GlobexId, PayrollProbeModule.ModuleCode, true);
        on.IsSuccessStatusCode.ShouldBeTrue(await on.Content.ReadAsStringAsync());
        var allowed = await admin.Http.GetAsync("/api/v1/probe/payroll");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await admin.Http.GetStringAsync("/api/v1/me/permissions")).ShouldContain(PayrollProbeModule.ViewPermission);

        (await SetModuleAsync(platform, f.GlobexId, PayrollProbeModule.ModuleCode, false)).IsSuccessStatusCode.ShouldBeTrue();
        (await admin.Http.GetAsync("/api/v1/probe/payroll")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EntitlementIsPerTenant()
    {
        using var platform = await f.PlatformAdminAsync();
        using var acme = await f.AcmeAdminAsync();
        (await SetModuleAsync(platform, f.GlobexId, PayrollProbeModule.ModuleCode, true)).IsSuccessStatusCode.ShouldBeTrue();
        try
        {
            (await acme.Http.GetAsync("/api/v1/probe/payroll")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SetModuleAsync(platform, f.GlobexId, PayrollProbeModule.ModuleCode, false);
        }
    }

    [Fact]
    public async Task CoreModulesCannotBeDisabled()
    {
        using var platform = await f.PlatformAdminAsync();
        var r = await SetModuleAsync(platform, f.AcmeId, IdentityModule.ModuleCode, false);
        r.IsSuccessStatusCode.ShouldBeFalse();
        using var acme = await f.AcmeAdminAsync();
        (await acme.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantUserCannotChangeEntitlements()
    {
        using var acme = await f.AcmeAdminAsync();
        (await SetModuleAsync(acme, f.AcmeId, PayrollProbeModule.ModuleCode, true)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
