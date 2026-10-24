using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hrms.Application.Abstractions;
using Hrms.Application.Tenancy;
using Hrms.Domain.Platform;
using Hrms.Infrastructure.Jobs;
using Hrms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hrms.Testing;

public sealed partial class CapturingEmailTransport : IEmailTransport
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public ConcurrentQueue<string> Sent { get; } = new();

    public Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        Sent.Enqueue(JsonSerializer.Serialize(email, Json));
        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }

    public string? LatestToken(string recipient) =>
        Sent.Reverse().Where(m => m.Contains(recipient, StringComparison.OrdinalIgnoreCase))
            .Select(m => TokenRegex().Match(m)).Where(m => m.Success).Select(m => Uri.UnescapeDataString(m.Groups[1].Value)).FirstOrDefault();

    [GeneratedRegex("token=([A-Za-z0-9%_\\-\\.~]+)")]
    private static partial Regex TokenRegex();
}

public sealed class TestFaultInjector : IProvisioningFaultInjector
{
    public string? FailAtStep { get; set; }
    public int FailuresRemaining { get; set; }

    public void BeforeStep(Guid tenantId, string step, int attempt)
    {
        if (step == FailAtStep && FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException($"Injected failure at {step}.");
        }
    }
}

/// <summary>
/// One PostgreSQL container, one API host, one platform DB and two provisioned tenants (acme, globex) with real per-tenant databases.
/// </summary>
public sealed class HrmsFixture : IAsyncLifetime
{
    public const string PlatformAdminEmail = "admin@hrms.test";
    public const string PlatformAdminPassword = "Platform-Admin-Passw0rd!";
    public const string Password = "Tenant-User-Passw0rd!";
    public const string TenantAuth = "/api/v1/auth";
    public const string PlatformAuth = "/api/v1/platform/auth";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").WithUsername("postgres").WithPassword("postgres").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public CapturingEmailTransport Emails { get; } = new();
    public TestFaultInjector Faults { get; } = new();
    public Guid AcmeId { get; private set; }
    public Guid GlobexId { get; private set; }

    public string ServerCs(string database) => $"Host={_pg.Hostname};Port={_pg.GetMappedPublicPort(5432)};Database={database};Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__Platform", ServerCs("hrms_platform"));
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "");
        Environment.SetEnvironmentVariable("Secrets__MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("Jobs__Mode", "InProcess");
        Environment.SetEnvironmentVariable("Identity__LoginRateLimitPerIpPerMinute", "100000");
        Environment.SetEnvironmentVariable("Identity__LoginRateLimitPerAccountPerMinute", "100000");
        Environment.SetEnvironmentVariable("Provisioning__SslMode", "Disable");
        Environment.SetEnvironmentVariable("FileStorage__LocalRootPath", Path.Combine(Path.GetTempPath(), "hrms-test-files-" + Guid.NewGuid().ToString("N")));

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IEmailTransport>();
                s.AddSingleton<IEmailTransport>(Emails);
                s.RemoveAll<IProvisioningFaultInjector>();
                s.AddSingleton<IProvisioningFaultInjector>(Faults);
                s.AddSingleton<Hrms.Application.Modules.IModuleDefinition, PayrollProbeModule>();
                s.AddControllers().AddApplicationPart(typeof(PayrollProbeController).Assembly);
            });
        });
        _ = Factory.Server;

        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<PlatformDbContext>().Database.MigrateAsync();
            var seed = sp.GetRequiredService<PlatformSeedService>();
            await seed.SeedAsync(default);
            await seed.EnsureClusterAsync("default", _pg.Hostname, _pg.GetMappedPublicPort(5432), "postgres", "postgres", default);
            await seed.EnsurePlatformAdminAsync(PlatformAdminEmail, "Super Admin", PlatformAdminPassword, default);
        }

        AcmeId = await ProvisionAsync("acme", "Acme Corp", "admin@acme.test");
        GlobexId = await ProvisionAsync("globex", "Globex Inc", "admin@globex.test");
        await AcceptInvitationAsync("acme", "admin@acme.test");
        await AcceptInvitationAsync("globex", "admin@globex.test");
    }

    public async Task<Guid> ProvisionAsync(string code, string name, string adminEmail, bool expectSuccess = true)
    {
        using var scope = Factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
        var r = await svc.CreateTenantAsync(new CreateTenantCommand(code, name, adminEmail, "Admin " + code, null), default);
        if (!r.IsSuccess)
        {
            throw new InvalidOperationException($"{r.Error!.Code}: {r.Error.Message}");
        }

        var status = await svc.RunAsync(r.Value.OperationId, default);
        await DrainAsync();
        if (expectSuccess && status != ProvisioningStatus.Succeeded)
        {
            throw new InvalidOperationException($"Provisioning {code} ended {status}.");
        }

        return r.Value.TenantId;
    }

    public async Task<ProvisioningStatus> RetryProvisioningAsync(Guid tenantId)
    {
        using var scope = Factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
        var r = await svc.PrepareRetryAsync(tenantId, default);
        var status = await svc.RunAsync(r.Value, default);
        await DrainAsync();
        return status;
    }

    public Task DrainAsync() => Factory.Services.GetRequiredService<InProcessJobQueue>().DrainAsync(TimeSpan.FromSeconds(30));

    public async Task<string> TokenForAsync(string email)
    {
        await DrainAsync();
        return Emails.LatestToken(email) ?? throw new InvalidOperationException($"No token email captured for {email}.");
    }

    public HrmsClient Client(string host, bool cookies = true) =>
        new(Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{host}"), HandleCookies = cookies, AllowAutoRedirect = false }));

    public async Task AcceptInvitationAsync(string tenantCode, string email)
    {
        using var c = Client($"{tenantCode}.localhost");
        await c.CsrfAsync(TenantAuth);
        var r = await c.Http.PostAsJsonAsync($"{TenantAuth}/reset-password", new { token = await TokenForAsync(email), newPassword = Password });
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Invitation for {email} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }
    }

    public async Task<HrmsClient> TenantLoginAsync(string tenantCode, string email, string password = Password)
    {
        var c = Client($"{tenantCode}.localhost");
        var r = await c.LoginAsync(TenantAuth, email, password);
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login {email}@{tenantCode} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }

        return c;
    }

    public Task<HrmsClient> AcmeAdminAsync() => TenantLoginAsync("acme", "admin@acme.test");

    public Task<HrmsClient> GlobexAdminAsync() => TenantLoginAsync("globex", "admin@globex.test");

    public async Task<HrmsClient> PlatformAdminAsync()
    {
        var c = Client("admin.localhost");
        (await c.LoginAsync(PlatformAuth, PlatformAdminEmail, PlatformAdminPassword)).EnsureSuccessStatusCode();
        return c;
    }

    /// <summary>Creates an invited tenant user through the API, accepts the invitation and returns a logged-in client.</summary>
    public async Task<(Guid UserId, HrmsClient Client)> CreateTenantUserAsync(HrmsClient admin, string tenantCode, string email, params Guid[] roleIds)
    {
        var r = await admin.Http.PostAsJsonAsync("/api/v1/users", new { email, displayName = email, employeeId = (Guid?)null, roleIds });
        var body = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Create user failed: {(int)r.StatusCode} {body}");
        }

        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        await AcceptInvitationAsync(tenantCode, email);
        return (id, await TenantLoginAsync(tenantCode, email));
    }

    public async Task<string> TenantDatabaseAsync(string code)
    {
        await using var conn = new NpgsqlConnection(ServerCs("hrms_platform"));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select d.database_name from platform.tenant_databases d join platform.tenants t on t.id = d.tenant_id where t.code = @c", conn);
        cmd.Parameters.AddWithValue("c", code);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<T?> ScalarAsync<T>(string database, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(ServerCs(database));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

public sealed class HrmsClient(HttpClient http) : IDisposable
{
    public HttpClient Http { get; } = http;

    public async Task CsrfAsync(string authPrefix)
    {
        var j = await Http.GetFromJsonAsync<JsonElement>($"{authPrefix}/csrf");
        SetCsrf(j.GetProperty("token").GetString()!);
    }

    public void SetCsrf(string token)
    {
        Http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    public async Task<HttpResponseMessage> LoginAsync(string authPrefix, string email, string password)
    {
        await CsrfAsync(authPrefix);
        var r = await Http.PostAsJsonAsync($"{authPrefix}/login", new { email, password });
        if (r.IsSuccessStatusCode && (await r.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("csrfToken", out var t))
        {
            SetCsrf(t.GetString()!);
        }

        return r;
    }

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var r = await Http.GetAsync(url);
        var body = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET {url} -> {(int)r.StatusCode} {body}");
        }

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task<Guid> PostForIdAsync(string url, object body)
    {
        var r = await Http.PostAsJsonAsync(url, body);
        var text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"POST {url} -> {(int)r.StatusCode} {text}");
        }

        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    public void Dispose() => Http.Dispose();
}
