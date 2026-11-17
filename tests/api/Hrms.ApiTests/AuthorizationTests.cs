using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Identity;
using Hrms.Testing;
using P = Hrms.Application.Modules.FoundationPermissions;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class AuthorizationTests(HrmsFixture f)
{
    private static string NewEmail() => $"u-{Guid.NewGuid():N}@acme.test";

    private static async Task<Guid> RoleIdAsync(HrmsClient admin, string code) =>
        (await admin.GetJsonAsync("/api/v1/roles")).EnumerateArray().First(r => r.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    [Fact]
    public async Task UserWithoutPermissionIsForbidden()
    {
        using var admin = await f.AcmeAdminAsync();
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", NewEmail());
        using (user)
        {
            (await user.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.GetAsync("/api/v1/roles")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task GrantOverrideAllowsAccess()
    {
        using var admin = await f.AcmeAdminAsync();
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", NewEmail());
        using (user)
        {
            (await user.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new { permissionCode = P.UserView, effect = "Grant", scopeLevel = "Tenant", reason = "test", validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });
            (await user.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task DenyOverrideBeatsRoleGrant()
    {
        using var admin = await f.AcmeAdminAsync();
        var (userId, user) = await f.CreateTenantUserAsync(admin, "acme", NewEmail(), await RoleIdAsync(admin, SystemRoles.TenantAdmin));
        using (user)
        {
            (await user.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
            await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new { permissionCode = P.UserView, effect = "Deny", reason = "test" });
            (await user.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Http.GetAsync("/api/v1/roles")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task GrantCeilingPreventsPrivilegeEscalation()
    {
        using var admin = await f.AcmeAdminAsync();
        var roleId = await admin.PostForIdAsync("/api/v1/roles", new
        {
            code = TestData.Unique("RM"),
            name = "Role manager",
            description = (string?)null,
            permissions = new[] { new { permissionCode = P.RoleView, scopeLevel = "Tenant" }, new { permissionCode = P.RoleCreate, scopeLevel = "Tenant" } },
        });
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", NewEmail(), roleId);
        using (user)
        {
            var escalate = await user.Http.PostAsJsonAsync("/api/v1/roles", new
            {
                code = TestData.Unique("ESC"), name = "Escalated", description = (string?)null,
                permissions = new[] { new { permissionCode = P.UserCreate, scopeLevel = "Tenant" } },
            });
            escalate.IsSuccessStatusCode.ShouldBeFalse();
            ((int)escalate.StatusCode).ShouldBeOneOf(403, 422);

            var allowed = await user.Http.PostAsJsonAsync("/api/v1/roles", new
            {
                code = TestData.Unique("OK"), name = "Within ceiling", description = (string?)null,
                permissions = new[] { new { permissionCode = P.RoleView, scopeLevel = "Tenant" } },
            });
            allowed.IsSuccessStatusCode.ShouldBeTrue(await allowed.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task NavigationOnlyContainsPermittedEntries()
    {
        using var admin = await f.AcmeAdminAsync();
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", NewEmail());
        using (user)
        {
            var adminNav = (await admin.GetJsonAsync("/api/v1/me/navigation")).GetRawText();
            var userNav = (await user.GetJsonAsync("/api/v1/me/navigation")).GetRawText();
            adminNav.Length.ShouldBeGreaterThan(userNav.Length);
            JsonDocument.Parse(userNav).RootElement.GetRawText().ShouldNotContain("/admin/users");
        }
    }
}
