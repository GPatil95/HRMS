using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hrms.Application.Modules;
using Hrms.Testing;

namespace Hrms.ApiTests;

[Collection(HrmsCollection.Name)]
public sealed class FileIsolationTests(HrmsFixture f)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private static async Task<HttpResponseMessage> UploadAsync(HrmsClient c, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(FilesModule.GeneralAttachmentPurpose), "purpose");
        return await c.Http.PostAsync("/api/v1/files", form);
    }

    private static async Task<Guid> UploadOkAsync(HrmsClient c)
    {
        var r = await UploadAsync(c, Pdf, "doc.pdf");
        var body = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.ShouldBeTrue(body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task FileFromAnotherTenantCannotBeReadOrDownloaded()
    {
        using var acme = await f.AcmeAdminAsync();
        using var globex = await f.GlobexAdminAsync();
        var id = await UploadOkAsync(acme);

        var own = await acme.Http.GetAsync($"/api/v1/files/{id}/content");
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);

        (await globex.Http.GetAsync($"/api/v1/files/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.Http.GetAsync($"/api/v1/files/{id}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.Http.GetAsync($"/api/v1/files/{id}/content?tenantId={f.AcmeId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.Http.DeleteAsync($"/api/v1/files/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.Http.GetAsync($"/api/v1/files/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadRequiresPermission()
    {
        using var admin = await f.AcmeAdminAsync();
        var id = await UploadOkAsync(admin);
        var (_, user) = await f.CreateTenantUserAsync(admin, "acme", $"file-{Guid.NewGuid():N}@acme.test");
        using (user)
        {
            (await user.Http.GetAsync($"/api/v1/files/{id}/content")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task SpoofedSignaturePathTraversalAndMalwareAreRejected()
    {
        using var admin = await f.AcmeAdminAsync();
        (await UploadAsync(admin, Encoding.ASCII.GetBytes("MZ not a pdf"), "fake.pdf")).IsSuccessStatusCode.ShouldBeFalse();
        (await UploadAsync(admin, Pdf, "file.exe")).IsSuccessStatusCode.ShouldBeFalse();
        var eicar = Encoding.ASCII.GetBytes("%PDF-1.4\nX5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*\n");
        (await UploadAsync(admin, eicar, "eicar.pdf")).IsSuccessStatusCode.ShouldBeFalse();

        var traversal = await UploadAsync(admin, Pdf, "../../../etc/passwd.pdf");
        if (traversal.IsSuccessStatusCode)
        {
            var meta = await traversal.Content.ReadAsStringAsync();
            meta.ShouldNotContain("..");
        }
    }
}
