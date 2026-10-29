using System.Net;
using System.Net.Http.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class TenantIsolationTests(HrmsFixture f)
{
    [Fact]
    public async Task UnknownHostIsRejected()
    {
        using var c = f.Client("unknown.localhost");
        (await c.Http.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlatformApiIsNotServedOnTenantHostAndViceVersa()
    {
        using var acme = await f.AcmeAdminAsync();
        (await acme.Http.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var platform = await f.PlatformAdminAsync();
        (await platform.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await platform.Http.GetAsync("/api/v1/employees")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SessionCookieFromOneTenantIsRejectedOnAnother()
    {
        var (cookies, _) = await TestData.RawLoginAsync(f, "acme.localhost", "admin@acme.test");
        (await TestData.SendWithCookiesAsync(f, "acme.localhost", HttpMethod.Get, "/api/v1/users", cookies)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TestData.SendWithCookiesAsync(f, "globex.localhost", HttpMethod.Get, "/api/v1/users", cookies)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TestData.SendWithCookiesAsync(f, "globex.localhost", HttpMethod.Get, "/api/v1/auth/me", cookies)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordIdFromAnotherTenantIsNotFound()
    {
        using var acme = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();
        var employeeId = await TestData.CreateEmployeeAsync(acme);
        (await acme.Http.GetAsync($"/api/v1/employees/{employeeId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await globex.Http.GetAsync($"/api/v1/employees/{employeeId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantIdInQueryHeaderAndRouteIsIgnored()
    {
        using var acme = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();
        var employeeId = await TestData.CreateEmployeeAsync(acme);
        globex.Http.DefaultRequestHeaders.Add("X-Tenant-Id", f.AcmeId.ToString());
        var list = await globex.Http.GetStringAsync($"/api/v1/employees?tenantId={f.AcmeId}&pageSize=200");
        list.ShouldNotContain(employeeId.ToString());
        (await globex.Http.GetAsync($"/api/v1/employees/{employeeId}?tenantId={f.AcmeId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantIdInRequestBodyIsIgnored()
    {
        using var acme = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();
        var code = TestData.Unique("BODY");
        var r = await globex.Http.PostAsJsonAsync("/api/v1/org/companies", new { code, name = "Body test", legalName = (string?)null, tenantId = f.AcmeId });
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());
        (await globex.Http.GetStringAsync("/api/v1/org/companies")).ShouldContain(code);
        (await acme.Http.GetStringAsync("/api/v1/org/companies")).ShouldNotContain(code);
    }

    [Fact]
    public async Task TenantDatabasesArePhysicallySeparateAndIdentityStamped()
    {
        var acmeDb = await f.TenantDatabaseAsync("acme");
        var globexDb = await f.TenantDatabaseAsync("globex");
        acmeDb.ShouldNotBe(globexDb);
        (await f.ScalarAsync<string>(acmeDb, "select tenant_id::text from ops.tenant_identity")).ShouldBe(f.AcmeId.ToString());
        (await f.ScalarAsync<string>(globexDb, "select tenant_id::text from ops.tenant_identity")).ShouldBe(f.GlobexId.ToString());
    }
}
