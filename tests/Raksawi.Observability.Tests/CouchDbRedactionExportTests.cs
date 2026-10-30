using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// C5: a CouchDB document id never reaches export. These drive a real
/// HttpClient call through the production instrumentation hook, the allowlist
/// processor and an exporter, and assert on what the exporter received — the
/// leaks these guard passed the unit tests on either side of them.
/// </summary>
/// <remarks>
/// One class, so xunit runs these serially: HttpClient instrumentation listens
/// process-wide, and a second provider alive in parallel would run its own
/// processors over these spans.
/// </remarks>
public sealed class CouchDbRedactionExportTests : IDisposable
{
    private const string DocumentId = "doc-4f1c9a";

    private readonly HttpListener _server = new();
    private readonly Task _serving;
    private readonly int _port;

    public CouchDbRedactionExportTests()
    {
        _port = FreePort();
        _server.Prefixes.Add($"http://localhost:{_port}/");
        _server.Start();
        _serving = ServeAsync();
    }

    public void Dispose()
    {
        _server.Close();
        _ = _serving.Wait(TimeSpan.FromSeconds(5));
    }

    private Uri DocumentUri => new($"http://localhost:{_port}/kyc/{DocumentId}");

    [Fact]
    public async Task A_configured_host_exports_the_redacted_url()
    {
        var spans = await ExportAsync(Options("localhost"));

        var span = Assert.Single(spans);
        Assert.Equal($"http://localhost:{_port}/kyc/{{docid}}", span["url.full"]);
    }

    [Fact]
    public async Task A_mixed_case_configured_host_never_exports_the_document_id()
    {
        // Uri.Host is lower-cased; configuration is whatever someone typed.
        AssertNoDocumentId(await ExportAsync(Options("LocalHost")));
    }

    [Fact]
    public async Task Turning_redaction_off_never_exports_the_document_id()
    {
        var options = Options("localhost");
        options.RedactCouchDbUrls = false;

        AssertNoDocumentId(await ExportAsync(options));
    }

    [Fact]
    public async Task A_later_rewrite_of_url_full_never_exports_the_document_id()
    {
        // A service's own processor, or a second enrich, running after ours.
        using var rewriter = new RewriteUrlProcessor(DocumentUri.ToString());

        AssertNoDocumentId(await ExportAsync(Options("localhost"), rewriter));
    }

    [Fact]
    public async Task An_unconfigured_host_exports_no_url()
    {
        var span = Assert.Single(await ExportAsync(Options()));

        Assert.False(span.ContainsKey("url.full"));
    }

    private static RaksawiObservabilityOptions Options(params string[] couchDbHosts)
    {
        var options = new RaksawiObservabilityOptions();
        foreach (var host in couchDbHosts)
        {
            options.CouchDbHosts.Add(host);
        }

        return options;
    }

    private async Task<List<Dictionary<string, object?>>> ExportAsync(
        RaksawiObservabilityOptions options,
        BaseProcessor<Activity>? beforeAllowlist = null)
    {
        var exported = new List<Dictionary<string, object?>>();

        var builder = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation(http => RaksawiPipeline.ConfigureHttpClientTracing(http, options));

        if (beforeAllowlist is not null)
        {
            builder = builder.AddProcessor(beforeAllowlist);
        }

        using (builder
            .AddProcessor(new AllowlistProcessor(AttributeAllowlist.FromLoadedAssemblies()))
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .Build())
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(DocumentUri);
            _ = response.EnsureSuccessStatusCode();
        }

        return exported;
    }

    private static void AssertNoDocumentId(List<Dictionary<string, object?>> spans)
    {
        // Without a span the assertion below holds vacuously.
        Assert.NotEmpty(spans);

        foreach (var tag in spans.SelectMany(span => span))
        {
            Assert.DoesNotContain(DocumentId, tag.Value as string ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var context = await _server.GetContextAsync();
                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }
        catch (HttpListenerException)
        {
            // Closed by Dispose.
        }
        catch (ObjectDisposedException)
        {
            // Closed by Dispose.
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class RewriteUrlProcessor(string rawUrl) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => _ = data.SetTag("url.full", rawUrl);
    }

    private sealed class SnapshotExporter(List<Dictionary<string, object?>> exported) : BaseExporter<Activity>
    {
        // A snapshot, not the Activity: later processors and disposal mutate it.
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                exported.Add(activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value));
            }

            return ExportResult.Success;
        }
    }
}
