using Hrms.Application.Abstractions;
using Hrms.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hrms.Infrastructure.Pdf;

/// <summary>
/// HTML→PDF via an internal Gotenberg sidecar (Chromium route). The service is only reachable inside
/// the docker network — never published — and rendered HTML is validated upstream to contain no
/// external URLs, so no remote fetching can be smuggled through it.
///
/// When no Gotenberg URL is configured the converter reports `pdf.unavailable` so issuance fails
/// predictably (generation_failed) instead of silently succeeding — PDF is mandatory.
/// </summary>
public sealed class GotenbergPdfService(IConfiguration config, ILogger<GotenbergPdfService> logger) : IPdfService
{
    private const int MaxHtmlBytes = 256 * 1024;
    private const int MaxPdfBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // One shared client: Gotenberg is internal-only and calls are bounded per conversion.
    private static readonly HttpClient Http = new() { Timeout = Timeout };

    public async Task<Result<byte[]>> RenderPdfAsync(string html, CancellationToken ct)
    {
        var baseUrl = config["Pdf:GotenbergUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Error.Rule("pdf.unavailable", "PDF service is not configured (Pdf:GotenbergUrl). Letter number stays reserved — retry once the PDF service is up.");
        }

        var htmlBytes = System.Text.Encoding.UTF8.GetBytes(html);
        if (htmlBytes.Length > MaxHtmlBytes)
        {
            return Error.Rule("pdf.html_too_large", $"HTML exceeds the {MaxHtmlBytes / 1024} KB limit.");
        }

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(htmlBytes) { Headers = { ContentType = new("text/html") } }, "files", "index.html");

            using var response = await Http.PostAsync($"{baseUrl.TrimEnd('/')}/forms/chromium/convert/html", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gotenberg returned {Status} for letter conversion", (int)response.StatusCode);
                return Error.Rule("pdf.conversion_failed", $"PDF conversion failed (HTTP {(int)response.StatusCode}). Letter number stays reserved — retry when the PDF service recovers.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > MaxPdfBytes)
            {
                return Error.Rule("pdf.output_invalid", $"PDF output invalid ({bytes.Length} bytes). Letter number stays reserved — retry available.");
            }

            if (bytes.Length < 5 || bytes[0] != (byte)'%' || bytes[1] != (byte)'P')
            {
                return Error.Rule("pdf.output_invalid", "PDF service returned non-PDF content. Letter number stays reserved — retry available.");
            }

            return bytes;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error.Rule("pdf.timeout", "PDF generation timed out after 15s. Letter number stays reserved — retry available.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Gotenberg unreachable at {Url}", baseUrl);
            return Error.Rule("pdf.unavailable", "PDF service unreachable. Letter number stays reserved — retry once the PDF service is up.");
        }
    }
}
