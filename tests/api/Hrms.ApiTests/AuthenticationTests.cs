using System.Net;
using System.Net.Http.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class AuthenticationTests(HrmsFixture f)
{
    [Fact]
    public async Task LoginWithoutCsrfTokenIsRejected()
    {
        using var c = f.Client("acme.localhost");
        var r = await c.Http.PostAsJsonAsync($"{HrmsFixture.TenantAuth}/login", new { email = "admin@acme.test", password = HrmsFixture.Password });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).ShouldContain("csrf.invalid");
    }

    [Fact]
    public async Task WrongPasswordIsRejected()
    {
        using var c = f.Client("acme.localhost");
        (await c.LoginAsync(HrmsFixture.TenantAuth, "admin@acme.test", "Wrong-Password-1!")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await c.LoginAsync(HrmsFixture.TenantAuth, "nobody@acme.test", "Wrong-Password-1!")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AccountLocksAfterRepeatedFailures()
    {
        using var admin = await f.AcmeAdminAsync();
        var email = $"lock-{Guid.NewGuid():N}@acme.test";
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", email);
        user.Dispose();
        using var c = f.Client("acme.localhost");
        for (var i = 0; i < 5; i++)
        {
            (await c.LoginAsync(HrmsFixture.TenantAuth, email, "Wrong-Password-1!")).IsSuccessStatusCode.ShouldBeFalse();
        }

        (await c.LoginAsync(HrmsFixture.TenantAuth, email, HrmsFixture.Password)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task LogoutRevokesTheServerSideSession()
    {
        var (cookies, csrf) = await TestData.RawLoginAsync(f, "acme.localhost", "admin@acme.test");
        (await TestData.SendWithCookiesAsync(f, "acme.localhost", HttpMethod.Get, "/api/v1/auth/me", cookies)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TestData.SendWithCookiesAsync(f, "acme.localhost", HttpMethod.Post, "/api/v1/auth/logout", cookies, csrf)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TestData.SendWithCookiesAsync(f, "acme.localhost", HttpMethod.Get, "/api/v1/auth/me", cookies)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TenantCredentialsDoNotAuthenticatePlatformRealm()
    {
        using var c = f.Client("admin.localhost");
        (await c.LoginAsync(HrmsFixture.PlatformAuth, "admin@acme.test", HrmsFixture.Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PlatformEndpointsRequirePlatformSession()
    {
        using var c = f.Client("admin.localhost");
        (await c.Http.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var platform = await f.PlatformAdminAsync();
        (await platform.Http.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
