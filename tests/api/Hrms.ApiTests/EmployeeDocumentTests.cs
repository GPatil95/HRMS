using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Employees;
using Hrms.Infrastructure.Messaging;
using Hrms.Infrastructure.Persistence;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class EmployeeDocumentTests(HrmsFixture f)
{
    private static readonly byte[] Pdf = "%PDF-1.4 fake"u8.ToArray();

    private async Task<(HrmsClient admin, Guid empId)> SetupAsync()
    {
        var admin = await f.TenantLoginAsync("acme", "admin@acme.test");
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Doc Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "Doc Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        return (admin, empId);
    }

    private static async Task<JsonElement> UploadAsync(HrmsClient admin, Guid empId, string categoryCode, object? extra = null)
    {
        var categories = await admin.GetJsonAsync("/api/v1/employee-docs/categories");
        var catId = categories.EnumerateArray().Single(c => c.GetProperty("code").GetString() == categoryCode).GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "doc.pdf");
        form.Add(new StringContent(catId.ToString()), "categoryId");
        if (extra is not null)
        {
            foreach (var p in extra.GetType().GetProperties())
            {
                if (p.GetValue(extra) is { } v)
                {
                    form.Add(new StringContent(v.ToString()!), p.Name);
                }
            }
        }

        var res = await admin.Http.PostAsync($"/api/v1/employees/{empId}/documents", form);
        res.IsSuccessStatusCode.ShouldBeTrue(await res.Content.ReadAsStringAsync());
        var docId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var docs = await admin.GetJsonAsync($"/api/v1/employees/{empId}/documents");
        return docs.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == docId);
    }

    [Fact]
    public async Task CategoryUpsertListAndStaleVersion()
    {
        var (admin, _) = await SetupAsync();
        var seeded = await admin.GetJsonAsync("/api/v1/employee-docs/categories");
        seeded.GetArrayLength().ShouldBeGreaterThanOrEqualTo(10); // baseline seeds

        var code = TestData.Unique("cat").ToLowerInvariant();
        var created = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/categories", new { code, name = "Custom Doc", requiredForOnboarding = false, confidential = true, trackExpiry = true, isActive = true });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await admin.GetJsonAsync("/api/v1/employee-docs/categories");
        var cat = list.EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);
        var rv = cat.GetProperty("rowVersion").GetUInt32();

        var stale = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/categories", new { code, name = "Renamed", isActive = true, rowVersion = rv + 5 });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var ok = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/categories", new { code, name = "Renamed", isActive = true, rowVersion = rv });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadVerifyAndReplaceDocument()
    {
        var (admin, empId) = await SetupAsync();
        var doc = await UploadAsync(admin, empId, "aadhaar", new { documentNumber = "1234", label = "Aadhaar front" });
        doc.GetProperty("status").GetString().ShouldBe("uploaded");
        doc.GetProperty("version").GetInt32().ShouldBe(1);
        var docId = doc.GetProperty("id").GetGuid();

        var verify = await admin.Http.PatchAsJsonAsync($"/api/v1/employees/{empId}/documents/{docId}/verify", new { status = "verified", note = "looks good" });
        verify.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Replacement bumps version and marks the old row replaced.
        var v2 = await UploadAsync(admin, empId, "aadhaar", new { replacesDocumentId = docId });
        v2.GetProperty("version").GetInt32().ShouldBe(2);

        var docs = await admin.GetJsonAsync($"/api/v1/employees/{empId}/documents");
        docs.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == docId).GetProperty("status").GetString().ShouldBe("replaced");

        var verifyReplaced = await admin.Http.PatchAsJsonAsync($"/api/v1/employees/{empId}/documents/{docId}/verify", new { status = "verified" });
        verifyReplaced.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ExpirySweepMarksExpired()
    {
        var (admin, empId) = await SetupAsync();
        var doc = await UploadAsync(admin, empId, "passport", new { expiresOn = DateOnly.FromDateTime(DateTime.Today.AddDays(30)).ToString("yyyy-MM-dd") });
        doc.GetProperty("status").GetString().ShouldBe("uploaded");
        var docId = doc.GetProperty("id").GetGuid();

        // Backdate the expiry below the sweep date, then run the sweep leg directly.
        var db = await f.TenantDatabaseAsync("acme");
        var n = await f.ScalarAsync<int>(db,
            "WITH u AS (UPDATE hr.employee_documents SET expires_on = @p WHERE id = @id RETURNING 1) SELECT count(*) FROM u",
            ("p", DateOnly.FromDateTime(DateTime.Today.AddDays(-2))), ("id", docId));
        n.ShouldBe(1);

        await using var ctx = TenantDbContextFactory.Create(f.ServerCs(db));
        var docs = new EmployeeDocumentService(ctx, null!, new ScribanTemplateRenderer(), new Hrms.Shared.Primitives.SystemClock());
        var expired = await docs.ExpireDueDocumentsAsync(DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        expired.ShouldBeGreaterThanOrEqualTo(1);

        var after = await admin.GetJsonAsync($"/api/v1/employees/{empId}/documents");
        after.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == docId).GetProperty("status").GetString().ShouldBe("expired");
    }

    [Fact]
    public async Task MissingOnboardingDocsTracksUploads()
    {
        var (admin, empId) = await SetupAsync();
        var missing = await admin.GetJsonAsync($"/api/v1/employees/{empId}/documents/missing");
        missing.EnumerateArray().Any(c => c.GetProperty("code").GetString() == "aadhaar").ShouldBeTrue();

        await UploadAsync(admin, empId, "aadhaar");
        var after = await admin.GetJsonAsync($"/api/v1/employees/{empId}/documents/missing");
        after.EnumerateArray().Any(c => c.GetProperty("code").GetString() == "aadhaar").ShouldBeFalse();
    }

    [Fact]
    public async Task LetterTemplateIssueAndRevoke()
    {
        var (admin, empId) = await SetupAsync();

        var typeCode = TestData.Unique("lt").ToLowerInvariant();
        var created = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-types", new { code = typeCode, name = "Reference Letter", isActive = true });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var tpl = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-types/{typeId}/templates",
            new { letterTypeId = typeId, name = "v1", subject = "Reference for {{ display_name }}", body = "To whom it may concern: {{ display_name }} ({{ employee_code }}) works at {{ company_name }} since {{ joining_date }}. Signed, {{ signatory_name }} on {{ date }}.", isActive = true });
        tpl.StatusCode.ShouldBe(HttpStatusCode.OK);

        var issue = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
        issue.StatusCode.ShouldBe(HttpStatusCode.OK);
        var letterId = (await issue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var letter = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");
        letter.GetProperty("subject").GetString().ShouldBe("Reference for Doc Emp");
        letter.GetProperty("renderedText").GetString()!.ShouldContain("Doc Emp");
        letter.GetProperty("status").GetString().ShouldBe("issued");
        letter.GetProperty("templateVersion").GetInt32().ShouldBe(1);

        // Issued letters are immutable snapshots: updating the template must not change the letter.
        var tpl2 = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-types/{typeId}/templates",
            new { letterTypeId = typeId, name = "v2", subject = "CHANGED", body = "CHANGED BODY", isActive = true });
        tpl2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reread = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");
        reread.GetProperty("subject").GetString().ShouldBe("Reference for Doc Emp");

        var revoke = await admin.Http.PostAsJsonAsync($"/api/v1/employees/letters/{letterId}/revoke", new { reason = "issued in error" });
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var revoked = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");
        revoked.GetProperty("status").GetString().ShouldBe("revoked");
    }

    [Fact]
    public async Task DownloadLetterReturnsRenderedText()
    {
        var (admin, empId) = await SetupAsync();
        var typeCode = TestData.Unique("lt").ToLowerInvariant();
        var created = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-types", new { code = typeCode, name = "Bonafide", isActive = true });
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-types/{typeId}/templates",
            new { letterTypeId = typeId, name = "v1", subject = "Bonafide {{ employee_code }}", body = "Body of {{ display_name }}", isActive = true });

        var issue = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
        var letterId = (await issue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var dl = await admin.Http.GetAsync($"/api/v1/employees/letters/{letterId}/download");
        dl.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await dl.Content.ReadAsStringAsync()).ShouldBe("Body of Doc Emp");
    }
}
