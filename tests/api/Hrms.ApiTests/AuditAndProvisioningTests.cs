using System.Net;
using System.Net.Http.Json;
using Hrms.Testing;
using Npgsql;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class AuditTests(HrmsFixture f)
{
    [Fact]
    public async Task ChangesAreAuditedWithActorSourceAndCorrelation()
    {
        using var admin = await f.AcmeAdminAsync();
        var cid = "test-" + Guid.NewGuid().ToString("N");
        admin.Http.DefaultRequestHeaders.Add("X-Correlation-Id", cid);
        await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("AUD"), name = "Audited", legalName = (string?)null });
        var db = await f.TenantDatabaseAsync("acme");
        (await f.ScalarAsync<long>(db, "select count(*) from audit.audit_entries where correlation_id = @c and actor_id is not null and source = 'web' and after is not null", ("c", cid))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AuditRowsCannotBeUpdatedOrDeletedInTheDatabase()
    {
        var db = await f.TenantDatabaseAsync("acme");
        await Should.ThrowAsync<PostgresException>(() => f.ScalarAsync<long>(db, "update audit.audit_entries set reason = 'tampered'"));
        await Should.ThrowAsync<PostgresException>(() => f.ScalarAsync<long>(db, "delete from audit.security_events"));
    }

    [Fact]
    public async Task FailedLoginWritesSecurityEvent()
    {
        var db = await f.TenantDatabaseAsync("acme");
        var before = await f.ScalarAsync<long>(db, "select count(*) from audit.security_events");
        using var c = f.Client("acme.localhost");
        await c.LoginAsync(HrmsFixture.TenantAuth, "admin@acme.test", "Wrong-Password-1!");
        (await f.ScalarAsync<long>(db, "select count(*) from audit.security_events")).ShouldBeGreaterThan(before);
    }
}

[Collection(HrmsCollection.Name)]
public sealed class ProvisioningTests(HrmsFixture f)
{
    [Fact]
    public async Task FailedStepIsRetriedIdempotently()
    {
        f.Faults.FailAtStep = "seed";
        f.Faults.FailuresRemaining = 1;
        var code = "init" + Guid.NewGuid().ToString("N")[..6];
        var tenantId = await f.ProvisionAsync(code, "Initech", $"admin@{code}.test", expectSuccess: false);
        using var c = f.Client($"{code}.localhost");
        (await c.Http.GetAsync("/api/v1/auth/me")).StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);

        (await f.RetryProvisioningAsync(tenantId)).ShouldBe(Hrms.Domain.Platform.ProvisioningStatus.Succeeded);
        (await f.ScalarAsync<string>(await f.TenantDatabaseAsync(code), "select tenant_id::text from ops.tenant_identity")).ShouldBe(tenantId.ToString());
        await f.AcceptInvitationAsync(code, $"admin@{code}.test");
        using var admin = await f.TenantLoginAsync(code, $"admin@{code}.test");
        (await admin.Http.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DuplicateTenantCodeIsRejected()
    {
        using var platform = await f.PlatformAdminAsync();
        var r = await platform.Http.PostAsJsonAsync("/api/v1/platform/tenants", new { code = "acme", name = "Dup", adminEmail = "dup@x.test", adminName = "Dup", planCode = (string?)null });
        r.IsSuccessStatusCode.ShouldBeFalse();
        ((int)r.StatusCode).ShouldBeOneOf(400, 409);
    }

    [Fact]
    public async Task TenantCreatedThroughPlatformApiIsProvisionedByBackgroundJob()
    {
        using var platform = await f.PlatformAdminAsync();
        var code = "api" + Guid.NewGuid().ToString("N")[..6];
        var r = await platform.Http.PostAsJsonAsync("/api/v1/platform/tenants", new { code, name = "Via API", adminEmail = $"admin@{code}.test", adminName = "Admin", planCode = (string?)null });
        r.StatusCode.ShouldBe(HttpStatusCode.Accepted, await r.Content.ReadAsStringAsync());
        await f.DrainAsync();
        using var c = f.Client($"{code}.localhost");
        var me = await c.Http.GetAsync("/api/v1/auth/me");
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await me.Content.ReadAsStringAsync());
    }
}
