using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hrms.Infrastructure.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Hrms.ApiTests;

/// <summary>GotenbergPdfService integration: failure modes exercised against a local TCP stub
/// (no Gotenberg needed) plus one real-container conversion when the image is available.</summary>
public sealed class GotenbergPdfTests(ITestOutputHelper output)
{
    private static GotenbergPdfService Svc(string? url) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = url })
            .Build(),
        NullLogger<GotenbergPdfService>.Instance);

    /// <summary>Minimal one-shot HTTP server on loopback: reads the request, replies with the
    /// given status/body — or stalls to simulate a hung Gotenberg.</summary>
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serve;
        public string Url { get; }

        public StubServer(int status, byte[]? body, int stallMs = 0)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _serve = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    var req = new byte[8192];
                    var got = new MemoryStream();
                    int n;
                    while ((n = await stream.ReadAsync(req)) > 0)
                    {
                        got.Write(req, 0, n);
                        if (Encoding.ASCII.GetString(got.GetBuffer(), 0, (int)got.Length).Contains("\r\n\r\n")) break;
                    }

                    if (stallMs > 0)
                    {
                        await Task.Delay(stallMs);
                        return;
                    }

                    var statusText = status == 200 ? "OK" : "Internal Server Error";
                    var payload = body ?? [];
                    var header = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {status} {statusText}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header);
                    await stream.WriteAsync(payload);
                }
                catch { /* client may have given up (timeout test) */ }
            });
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
        }
    }

    private static byte[] HttpBody(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task UnconfiguredUrlReportsUnavailable()
    {
        var r = await Svc(null).RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.unavailable");
    }

    [Fact]
    public async Task UnreachableHostReportsUnavailable()
    {
        // Port 1 is never listening → connection refused → HttpRequestException.
        var r = await Svc("http://127.0.0.1:1").RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.unavailable");
    }

    [Fact]
    public async Task NonPdfResponseRejected()
    {
        using var stub = new StubServer(200, HttpBody("<html>not a pdf</html>"));
        var r = await Svc(stub.Url).RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.output_invalid");
    }

    [Fact]
    public async Task EmptyResponseRejected()
    {
        using var stub = new StubServer(200, []);
        var r = await Svc(stub.Url).RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.output_invalid");
    }

    [Fact]
    public async Task Http500ReportsConversionFailed()
    {
        using var stub = new StubServer(500, HttpBody("boom"));
        var r = await Svc(stub.Url).RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.conversion_failed");
    }

    [Fact]
    public async Task OversizedHtmlRejectedBeforeHttp()
    {
        using var stub = new StubServer(200, HttpBody("%PDF-1.4\n"));
        var big = new string('x', 260 * 1024);
        var r = await Svc(stub.Url).RenderPdfAsync(big, CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.html_too_large");
    }

    [Fact(Timeout = 60000)]
    public async Task HungGotenbergReportsTimeout()
    {
        using var stub = new StubServer(200, null, stallMs: 120_000);
        var r = await Svc(stub.Url).RenderPdfAsync("<p>x</p>", CancellationToken.None);
        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("pdf.timeout");
    }

    [Fact]
    public async Task RealGotenbergConvertsHtmlToPdf()
    {
        IContainer g;
        try
        {
            g = new ContainerBuilder("gotenberg/gotenberg:8")
                .WithPortBinding(3000, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPort(3000).ForPath("/health")))
                .Build();
            await g.StartAsync();
        }
        catch (Exception ex)
        {
            // Gotenberg is not deployed in this environment — recorded as not-run, not failed.
            output.WriteLine($"Skipped: Gotenberg container unavailable ({ex.Message})");
            return;
        }

        try
        {
            var url = $"http://{g.Hostname}:{g.GetMappedPublicPort(3000)}";
            var r = await Svc(url).RenderPdfAsync(
                "<html><body><h1>M2.7 test</h1><p>Hello</p></body></html>", CancellationToken.None);
            r.IsSuccess.ShouldBeTrue(r.Error?.Message);
            r.Value.Take(4).ShouldBe("%PDF"u8.ToArray());
            r.Value.Length.ShouldBeGreaterThan(500);
        }
        finally
        {
            await g.DisposeAsync();
        }
    }
}
