using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hrms.Testing;
using P = Hrms.Application.Modules.FoundationPermissions;

namespace Hrms.ApiTests;

/// <summary>M2.7 V2: template lifecycle, applicability, sanitization, numbering, issuance
/// failure/retry semantics, immutable snapshots and admin permissions.</summary>
[Collection(HrmsCollection.Name)]
public sealed class LetterV2Tests(HrmsFixture f)
{
    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private async Task<(HrmsClient admin, Guid empId, Guid companyId, Guid branchId)> SetupAsync()
    {
        var admin = await f.TenantLoginAsync("acme", "admin@acme.test");
        var companyId = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Letter Co", legalName = (string?)null });
        var branchId = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId, code = TestData.Unique("B"), name = "HQ" });
        var empId = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "V2 Emp", workEmail = (string?)null, branchId, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        return (admin, empId, companyId, branchId);
    }

    private static async Task<Guid> NewTypeAsync(HrmsClient admin, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = TestData.Unique("lt").ToLowerInvariant(), ["name"] = "V2 Type", ["isActive"] = true };
        if (extra is not null)
        {
            foreach (var p in extra.GetType().GetProperties())
            {
                body[p.Name[0].ToString().ToLowerInvariant() + p.Name[1..]] = p.GetValue(extra);
            }
        }

        var res = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-types", body);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> NewFamilyAsync(HrmsClient admin, Guid typeId, string name = "main")
    {
        var res = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-template-families", new { letterTypeId = typeId, name });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> NewDraftAsync(HrmsClient admin, Guid familyId, object version, Guid? baseTemplateId = null)
    {
        var url = $"/api/v1/employee-docs/letter-template-families/{familyId}/versions" + (baseTemplateId is { } b ? $"?baseTemplateId={b}" : "");
        var res = await admin.Http.PostAsJsonAsync(url, version);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object Draft(string subject, Guid? companyId = null, Guid? branchId = null, Guid? letterheadId = null, string? body = null) =>
        new { name = "v", subject, body = body ?? $"Body for {{{{ display_name }}}}", effectiveFrom = Today(), effectiveTo = (string?)null, companyId, branchId, letterheadId };

    private static async Task<Guid> PublishedAsync(HrmsClient admin, Guid familyId, object version)
    {
        var id = await NewDraftAsync(admin, familyId, version);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return id;
    }

    private static async Task<JsonElement> IssueAsync(HrmsClient admin, Guid empId, Guid typeId)
    {
        var res = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement[]> VersionsAsync(HrmsClient admin, Guid familyId) =>
        (await admin.GetJsonAsync($"/api/v1/employee-docs/letter-template-families/{familyId}/versions"))
            .EnumerateArray().ToArray();

    [Fact]
    public async Task PublishedTemplateIsImmutableDraftFromPublishedSupersedes()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        var v1 = await PublishedAsync(admin, fam, Draft("First {{ display_name }}"));

        // Published versions are immutable.
        var put = await admin.Http.PutAsJsonAsync($"/api/v1/employee-docs/letter-templates/{v1}", Draft("CHANGED"));
        put.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Editing flows through a superseding draft: version 2 linked to v1.
        var v2 = await NewDraftAsync(admin, fam, Draft("Second {{ display_name }}"), baseTemplateId: v1);
        var versions = await VersionsAsync(admin, fam);
        versions.Single(v => v.GetProperty("id").GetGuid() == v2).GetProperty("version").GetInt32().ShouldBe(2);
        versions.Single(v => v.GetProperty("id").GetGuid() == v2).GetProperty("supersedesId").GetGuid().ShouldBe(v1);

        // Publishing v2 retires v1.
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{v2}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await VersionsAsync(admin, fam);
        after.Single(v => v.GetProperty("id").GetGuid() == v1).GetProperty("status").GetString().ShouldBe("retired");
        after.Single(v => v.GetProperty("id").GetGuid() == v2).GetProperty("status").GetString().ShouldBe("published");
    }

    [Fact]
    public async Task ApplicabilityPrefersBranchThenCompanyThenTenant()
    {
        var admin = await f.TenantLoginAsync("acme", "admin@acme.test");
        var c1 = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "C1", legalName = (string?)null });
        var c2 = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "C2", legalName = (string?)null });
        var b1 = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = c1, code = TestData.Unique("B"), name = "B1" });
        var b2 = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = c1, code = TestData.Unique("B"), name = "B2" });
        var b3 = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = c2, code = TestData.Unique("B"), name = "B3" });
        async Task<Guid> Emp(Guid b) => await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = " ", displayName = "E", workEmail = (string?)null, branchId = b, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        var e1 = await Emp(b1); var e2 = await Emp(b2); var e3 = await Emp(b3);

        var typeId = await NewTypeAsync(admin);
        await PublishedAsync(admin, await NewFamilyAsync(admin, typeId, "tw"), Draft("TENANT WIDE"));
        await PublishedAsync(admin, await NewFamilyAsync(admin, typeId, "co"), Draft("COMPANY", companyId: c1));
        await PublishedAsync(admin, await NewFamilyAsync(admin, typeId, "br"), Draft("BRANCH", companyId: c1, branchId: b1));

        async Task<string> Subject(Guid emp)
        {
            var r = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{emp}/letters/preview", new { letterTypeId = typeId });
            r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subject").GetString()!;
        }

        (await Subject(e1)).ShouldBe("BRANCH");
        (await Subject(e2)).ShouldBe("COMPANY");
        (await Subject(e3)).ShouldBe("TENANT WIDE");
    }

    [Fact]
    public async Task SameScopeEffectiveOverlapPublishRejected()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string D(int days) => today.AddDays(days).ToString("yyyy-MM-dd");
        object V(string from, string? to) => new { name = "v", subject = "S", body = "B", effectiveFrom = from, effectiveTo = to, companyId = (Guid?)null, branchId = (Guid?)null, letterheadId = (Guid?)null };

        // Family A published for [today, today+30).
        await PublishedAsync(admin, await NewFamilyAsync(admin, typeId, "a"), V(D(0), D(30)));

        // A second family at the same (type, tenant-wide) scope overlapping that window: 409.
        var famB = await NewFamilyAsync(admin, typeId, "b");
        var overlapping = await NewDraftAsync(admin, famB, V(D(15), null));
        var publish = await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{overlapping}/publish", null);
        publish.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A disjoint window starting where A ends ([to) is exclusive) still publishes.
        var future = await NewDraftAsync(admin, famB, V(D(30), null));
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{future}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TemplateInputValidationRejectsMismatchAndUnknownFields()
    {
        var (admin, _, companyId, branchId) = await SetupAsync();
        var other = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Other", legalName = (string?)null });
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        // Branch belongs to companyId, not `other` — ownership mismatch rejected.
        var mismatch = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", companyId: other, branchId: branchId));
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Unknown and salary-gated merge fields are rejected.
        var unknown = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "Hello {{ not_a_field_xyz }}"));
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var salary = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "CTC: {{ salary_ctc }}"));
        salary.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SanitizerRejectsScriptHandlersAndExternalUrls()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        var handler = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "<p onclick=\"steal()\">hi</p>"));
        handler.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var script = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "<p>ok</p><script>alert(1)</script>"));
        script.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var external = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "<img src=\"https://evil.example/track.png\">"));
        external.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var styleUrl = await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            Draft("X", body: "<p style=\"background:url(http://evil.example/x)\">hi</p>"));
        styleUrl.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Same rules apply to letterhead header/footer HTML.
        var lh = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letterheads",
            new { name = "evil", companyId = (Guid?)null, branchId = (Guid?)null, logoFileId = (Guid?)null, headerHtml = "<img src=\"http://evil.example/x.png\">", footerHtml = (string?)null, marginMm = 15, isActive = true });
        lh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PreviewRendersWithoutPersistingALetter()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        await PublishedAsync(admin, await NewFamilyAsync(admin, typeId), Draft("Hi {{ display_name }}"));

        var preview = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters/preview", new { letterTypeId = typeId });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var dto = await preview.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("subject").GetString()!.ShouldContain("V2 Emp");
        dto.GetProperty("html").GetString()!.ShouldContain("V2 Emp");

        (await admin.GetJsonAsync($"/api/v1/employees/{empId}/letters")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task PdfFailureLeavesGenerationFailedRetryReusesNumber()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        await admin.PostForIdAsync($"/api/v1/employee-docs/letter-types/{typeId}/templates",
            new { letterTypeId = typeId, name = "v1", subject = "S", body = "B", isActive = true });

        f.Pdf.Fail = true;
        try
        {
            var issue = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
            issue.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

            // The failed issuance is recorded with its number consumed.
            var letters = await admin.GetJsonAsync($"/api/v1/employees/{empId}/letters");
            var failed = letters.EnumerateArray().Single();
            failed.GetProperty("status").GetString().ShouldBe("generation_failed");
            var number = failed.GetProperty("letterNumber").GetString()!;
            number.ShouldStartWith("LTR-");

            // Retry reuses the same allocated number and completes issuance.
            f.Pdf.Fail = false;
            var retry = await admin.Http.PostAsJsonAsync($"/api/v1/employees/letters/{failed.GetProperty("id").GetGuid()}/retry", new { });
            retry.StatusCode.ShouldBe(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
            var done = await retry.Content.ReadFromJsonAsync<JsonElement>();
            done.GetProperty("letterNumber").GetString().ShouldBe(number);
            done.GetProperty("status").GetString().ShouldBe("issued");
        }
        finally
        {
            f.Pdf.Fail = false;
        }
    }

    [Fact]
    public async Task ConcurrentIssuesAllocateDistinctNumbers()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        await admin.PostForIdAsync($"/api/v1/employee-docs/letter-types/{typeId}/templates",
            new { letterTypeId = typeId, name = "v1", subject = "S", body = "B", isActive = true });

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId }))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        var numbers = new HashSet<string>();
        foreach (var r in responses)
        {
            r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            numbers.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("letterNumber").GetString()!);
        }

        numbers.Count.ShouldBe(5);
    }

    [Fact]
    public async Task IssuedLetterCarriesImmutablePdfHashAndSnapshots()
    {
        var (admin, empId, companyId, branchId) = await SetupAsync();

        var sigRes = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-signatories",
            new { name = "Priya Shah", designation = "Head of HR", employeeId = (Guid?)null, isActive = true });
        sigRes.StatusCode.ShouldBe(HttpStatusCode.OK, await sigRes.Content.ReadAsStringAsync());
        var signatoryId = (await sigRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var lhRes = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letterheads",
            new { name = "Branded", companyId, branchId = (Guid?)null, logoFileId = (Guid?)null, headerHtml = "<p>HEADER</p>", footerHtml = "<p>FOOT</p>", marginMm = 20, isActive = true });
        lhRes.StatusCode.ShouldBe(HttpStatusCode.OK, await lhRes.Content.ReadAsStringAsync());
        var letterheadId = (await lhRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var typeId = await NewTypeAsync(admin, new { signatoryId });
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("Letter for {{ display_name }}", companyId, branchId, letterheadId));

        var issued = await IssueAsync(admin, empId, typeId);
        var letterId = issued.GetProperty("letterId").GetGuid();
        var letter = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");

        letter.GetProperty("signatoryName").GetString().ShouldBe("Priya Shah");
        letter.GetProperty("signatoryDesignation").GetString().ShouldBe("Head of HR");
        letter.GetProperty("letterheadSnapshot").GetString()!.ShouldContain("Branded");
        letter.GetProperty("issueDate").GetString().ShouldBe(Today());
        letter.GetProperty("issueTz").GetString()!.ShouldNotBeNullOrEmpty();

        // The stored sha-256 matches the immutable PDF bytes returned by download.
        var dl = await admin.Http.GetAsync($"/api/v1/employees/letters/{letterId}/download");
        dl.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await dl.Content.ReadAsByteArrayAsync();
        bytes.Take(4).ShouldBe("%PDF"u8.ToArray());
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            .ShouldBe(letter.GetProperty("pdfSha256").GetString());
    }

    [Fact]
    public async Task LetterAdminEndpointsRequireManagePermissions()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        var tpl = await NewDraftAsync(admin, fam, Draft("S"));

        var (userId, scoped) = await f.CreateTenantUserAsync(admin, "acme", $"{TestData.Unique("hr")}@acme.test".ToLowerInvariant());
        // View-only grant at tenant scope — no manage/publish/issue rights.
        await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new
        {
            permissionCode = P.LettersView, effect = "Grant", scopeLevel = "Tenant",
            targetKind = (string?)null, targetIds = Array.Empty<Guid>(), reason = "view only",
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        });

        // Read endpoints work with view permission.
        (await scoped.Http.GetAsync("/api/v1/employee-docs/letter-template-families")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Every mutating/admin endpoint denies.
        (await scoped.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-template-families", new { letterTypeId = typeId, name = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.Http.PostAsJsonAsync("/api/v1/employee-docs/letterheads", new { name = "x", companyId = (Guid?)null, branchId = (Guid?)null, logoFileId = (Guid?)null, headerHtml = (string?)null, footerHtml = (string?)null, marginMm = 15, isActive = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-signatories", new { name = "x", designation = (string?)null, employeeId = (Guid?)null, isActive = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{tpl}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InvalidLifecycleTransitionsRejected()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        var draft = await NewDraftAsync(admin, fam, Draft("S"));

        // Retire on a draft is a withdraw — allowed and idempotent.
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{draft}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{draft}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A retired version can never be published.
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{draft}/publish", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);

        // Publish is idempotent; retired after publish stays retired.
        var d2 = await NewDraftAsync(admin, fam, Draft("S"));
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d2}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d2}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d2}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d2}/publish", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ManageWithoutPublishPermissionCannotPublish()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        var (userId, scoped) = await f.CreateTenantUserAsync(admin, "acme", $"{TestData.Unique("hr")}@acme.test".ToLowerInvariant());
        await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new
        {
            permissionCode = P.LettersTemplateManage, effect = "Grant", scopeLevel = "Tenant",
            targetKind = (string?)null, targetIds = Array.Empty<Guid>(), reason = "manage",
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        });

        // Manage covers draft CRUD but not publish/retire.
        var res = await scoped.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            new { name = "v", subject = "S", body = "B", effectiveFrom = Today(), effectiveTo = (string?)null });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var draftId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await scoped.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{draftId}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{draftId}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConcurrentOverlappingPublishesExactlyOneWins()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        // Two independent drafts at the same scope/window — no supersedes link.
        var d1 = await NewDraftAsync(admin, fam, Draft("S"));
        var d2 = await NewDraftAsync(admin, fam, Draft("S"));

        var results = await Task.WhenAll(
            admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d1}/publish", null),
            admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{d2}/publish", null));

        var codes = results.Select(r => r.StatusCode).ToArray();
        codes.Count(c => c == HttpStatusCode.NoContent).ShouldBe(1);
        codes.Count(c => c == HttpStatusCode.Conflict).ShouldBe(1);
    }

    [Fact]
    public async Task EffectiveDateBoundariesEnforced()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        // Future effectiveFrom — published but not yet in effect → preview/issue fail.
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        await PublishedAsync(admin, fam, new { name = "v", subject = "S", body = "B", effectiveFrom = tomorrow, effectiveTo = (string?)null });
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters/preview", new { letterTypeId = typeId }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SanitizerRejectsContainersAndJavascriptUrls()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        foreach (var body in new[]
        {
            "<iframe src=\"https://evil.example\"></iframe>",
            "<object data=\"x.swf\"></object>",
            "<embed src=\"x.swf\">",
            "<a href=\"javascript:alert(1)\">x</a>",
            "<form action=\"/steal\"><input name=\"p\"/></form>",
        })
        {
            (await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
                new { name = "v", subject = "S", body, effectiveFrom = Today(), effectiveTo = (string?)null }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task OversizedAndInvalidScribanTemplatesRejected()
    {
        var (admin, _, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);

        // > 256 KB body.
        var big = new string('x', 260 * 1024);
        (await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            new { name = "v", subject = "S", body = big, effectiveFrom = Today(), effectiveTo = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Unclosed Scriban expression.
        (await admin.Http.PostAsJsonAsync($"/api/v1/employee-docs/letter-template-families/{fam}/versions",
            new { name = "v", subject = "S", body = "{{ bogus(", effectiveFrom = Today(), effectiveTo = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FailedIssueConsumesNumberThenNextIssueContinues()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));

        f.Pdf.Fail = true;
        var fail = await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
        fail.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        f.Pdf.Fail = false;

        var ok = await IssueAsync(admin, empId, typeId);
        ok.GetProperty("letterNumber").GetString()!.ShouldNotEndWith("000001");
    }

    [Fact]
    public async Task RetryAndRevokeRejectedForWrongStatuses()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));

        var letterId = (await IssueAsync(admin, empId, typeId)).GetProperty("letterId").GetGuid();

        // Retry only applies to generation_failed.
        (await admin.Http.PostAsync($"/api/v1/employees/letters/{letterId}/retry", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);

        // Revoke twice.
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/letters/{letterId}/revoke", new { reason = "x" }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await admin.Http.PostAsJsonAsync($"/api/v1/employees/letters/{letterId}/revoke", new { reason = "x" }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReissueSupersedesWithNewNumberAndCurrentTemplate()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        var v1 = await PublishedAsync(admin, fam, Draft("subject one"));

        var first = await IssueAsync(admin, empId, typeId);
        var firstId = first.GetProperty("letterId").GetGuid();
        var firstNumber = first.GetProperty("letterNumber").GetString();

        // Retire v1 and publish a newer v2 — reissue must resolve to v2.
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{v1}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var v2 = await NewDraftAsync(admin, fam, Draft("subject two"), baseTemplateId: v1);
        (await admin.Http.PostAsync($"/api/v1/employee-docs/letter-templates/{v2}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var re = await admin.Http.PostAsync($"/api/v1/employees/letters/{firstId}/reissue", null);
        re.StatusCode.ShouldBe(HttpStatusCode.OK, await re.Content.ReadAsStringAsync());
        var second = await re.Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("letterNumber").GetString().ShouldNotBe(firstNumber);

        var oldLetter = await admin.GetJsonAsync($"/api/v1/employees/letters/{firstId}");
        oldLetter.GetProperty("status").GetString().ShouldBe("superseded");
        // Original number and file preserved.
        oldLetter.GetProperty("letterNumber").GetString().ShouldBe(firstNumber);
        oldLetter.GetProperty("fileId").GetGuid().ShouldNotBe(Guid.Empty);

        var newLetter = await admin.GetJsonAsync($"/api/v1/employees/letters/{second.GetProperty("letterId").GetGuid()}");
        newLetter.GetProperty("status").GetString().ShouldBe("issued");
        newLetter.GetProperty("subject").GetString().ShouldBe("subject two");
        newLetter.GetProperty("supersedesId").GetGuid().ShouldBe(firstId);

        // Reissue is rejected for a letter that is not issued/revoked.
        f.Pdf.Fail = true;
        await admin.Http.PostAsJsonAsync($"/api/v1/employees/{empId}/letters", new { letterTypeId = typeId });
        f.Pdf.Fail = false;
        var letters = await admin.GetJsonAsync($"/api/v1/employees/{empId}/letters");
        var failedId = letters.EnumerateArray().First(l => l.GetProperty("status").GetString() == "generation_failed").GetProperty("id").GetGuid();
        (await admin.Http.PostAsync($"/api/v1/employees/letters/{failedId}/reissue", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReissueRequiresReissuePermission()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));
        var letterId = (await IssueAsync(admin, empId, typeId)).GetProperty("letterId").GetGuid();

        var (userId, scoped) = await f.CreateTenantUserAsync(admin, "acme", $"{TestData.Unique("hr")}@acme.test".ToLowerInvariant());
        await admin.PostForIdAsync($"/api/v1/users/{userId}/overrides", new
        {
            permissionCode = P.LettersIssue, effect = "Grant", scopeLevel = "Tenant",
            targetKind = (string?)null, targetIds = Array.Empty<Guid>(), reason = "issue only",
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        });

        (await scoped.Http.PostAsync($"/api/v1/employees/letters/{letterId}/reissue", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrossTenantLetterAndTypeIdsAreNotFound()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var typeId = await NewTypeAsync(admin);
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));
        var letterId = (await IssueAsync(admin, empId, typeId)).GetProperty("letterId").GetGuid();

        var other = await f.TenantLoginAsync("globex", "admin@globex.test");
        (await other.Http.PostAsJsonAsync($"/api/v1/employees/{Guid.NewGuid()}/letters", new { letterTypeId = typeId }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        (await other.Http.GetAsync($"/api/v1/employees/letters/{letterId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Http.PostAsync($"/api/v1/employees/letters/{letterId}/reissue", null))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssuedLetterKeepsOriginalSignatoryAfterSignatoryEdit()
    {
        var (admin, empId, _, _) = await SetupAsync();
        var sigRes = await admin.Http.PostAsJsonAsync("/api/v1/employee-docs/letter-signatories",
            new { name = "Original Name", designation = "Original Desig", employeeId = (Guid?)null, isActive = true });
        var sigId = (await sigRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var typeId = await NewTypeAsync(admin, new { SignatoryId = sigId });
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));

        var letterId = (await IssueAsync(admin, empId, typeId)).GetProperty("letterId").GetGuid();

        // Edit the signatory master — the issued letter keeps its snapshot.
        (await admin.Http.PutAsJsonAsync($"/api/v1/employee-docs/letter-signatories/{sigId}",
            new { name = "Changed Name", designation = "Changed", employeeId = (Guid?)null, isActive = true, rowVersion = (uint?)null }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var letter = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");
        letter.GetProperty("signatoryName").GetString().ShouldBe("Original Name");
        letter.GetProperty("signatoryDesignation").GetString().ShouldBe("Original Desig");
    }

    [Fact]
    public async Task LegacyEmployeeSignatoryDualRead()
    {
        var (admin, empId, _, _) = await SetupAsync();
        // Signatory = the same employee record (legacy column), no signatory_id.
        var typeId = await NewTypeAsync(admin, new { SignatoryEmployeeId = empId });
        var fam = await NewFamilyAsync(admin, typeId);
        await PublishedAsync(admin, fam, Draft("S"));

        var letterId = (await IssueAsync(admin, empId, typeId)).GetProperty("letterId").GetGuid();
        var letter = await admin.GetJsonAsync($"/api/v1/employees/letters/{letterId}");
        letter.GetProperty("signatoryName").GetString().ShouldBe("V2 Emp");
    }
}
