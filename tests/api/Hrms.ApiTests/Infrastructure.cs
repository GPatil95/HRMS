using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Testing;

namespace Hrms.ApiTests;

[CollectionDefinition(Name)]
public sealed class HrmsCollection : ICollectionFixture<HrmsFixture>
{
    public const string Name = "hrms";
}

public static class TestData
{
    public static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 8, 40)].ToUpperInvariant();

    public static async Task<Guid> CreateEmployeeAsync(HrmsClient admin)
    {
        var company = await admin.PostForIdAsync("/api/v1/org/companies", new { code = Unique("C"), name = "Company", legalName = (string?)null });
        var branch = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = company, code = Unique("B"), name = "Branch" });
        return await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = Unique("E"), displayName = "Emp", workEmail = (string?)null, branchId = branch, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
    }

    /// <summary>Logs in without a cookie container so the raw session cookie can be replayed elsewhere.</summary>
    public static async Task<(string Cookies, string Csrf)> RawLoginAsync(HrmsFixture f, string host, string email)
    {
        using var c = f.Client(host, cookies: false);
        var csrfResp = await c.Http.GetAsync($"{HrmsFixture.TenantAuth}/csrf");
        var csrfCookie = Cookie(csrfResp, "hrms_csrf");
        var token = (await csrfResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{HrmsFixture.TenantAuth}/login") { Content = JsonContent.Create(new { email, password = HrmsFixture.Password }) };
        req.Headers.Add("Cookie", csrfCookie);
        req.Headers.Add("X-CSRF-TOKEN", token);
        var login = await c.Http.SendAsync(req);
        login.EnsureSuccessStatusCode();
        var newCsrf = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
        return ($"{csrfCookie}; {Cookie(login, "hrms_t")}", newCsrf);
    }

    public static async Task<HttpResponseMessage> SendWithCookiesAsync(HrmsFixture f, string host, HttpMethod method, string url, string cookies, string? csrf = null)
    {
        using var c = f.Client(host, cookies: false);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", cookies);
        if (csrf is not null)
        {
            req.Headers.Add("X-CSRF-TOKEN", csrf);
        }

        return await c.Http.SendAsync(req);
    }

    private static string Cookie(HttpResponseMessage r, string name) =>
        r.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0]).Last(v => v.StartsWith(name + "=", StringComparison.Ordinal));
}
