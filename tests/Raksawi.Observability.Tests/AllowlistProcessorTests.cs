using System.Diagnostics;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The processor is where ADR-0003 stops being a table and starts being a
/// control. These assert against a real <see cref="Activity"/>, because the
/// thing that matters is what survives to export, not what a predicate returns.
/// </summary>
public class AllowlistProcessorTests : IDisposable
{
    private readonly ActivitySource _source = new(nameof(AllowlistProcessorTests));
    private readonly ActivityListener _listener;

    public AllowlistProcessorTests()
    {
        // Without a listener sampling in, StartActivity returns null and every
        // test below would vacuously pass.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == nameof(AllowlistProcessorTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
        GC.SuppressFinalize(this);
    }

    private AllowlistProcessor CreateProcessor(params string[] couchDbHosts) =>
        new(AttributeAllowlist.ForDeclaredKeys("application.id", "correlation.id"), couchDbHosts);

    [Fact]
    public void Drops_an_undeclared_attribute_and_keeps_the_allowlisted_ones()
    {
        using var activity = _source.StartActivity("screen application")!;
        activity.SetTag("http.request.method", "POST");
        activity.SetTag("application.id", "app-1001");
        activity.SetTag("applicantCpr", "0101901234");   // 🔒 must not survive

        CreateProcessor().OnEnd(activity);

        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
        Assert.Equal("app-1001", activity.GetTagItem("application.id"));
        Assert.Null(activity.GetTagItem("applicantCpr"));
    }

    [Fact]
    public void Drops_a_carve_out_key_whose_family_is_otherwise_allowed()
    {
        using var activity = _source.StartActivity("call dependency")!;
        activity.SetTag("http.request.method", "GET");
        activity.SetTag("http.request.header.authorization", "Bearer abc123");   // 🔒 Class 4

        CreateProcessor().OnEnd(activity);

        Assert.Equal("GET", activity.GetTagItem("http.request.method"));
        Assert.Null(activity.GetTagItem("http.request.header.authorization"));
    }

    [Fact]
    public void Drops_url_full_on_an_ordinary_http_span()
    {
        using var activity = _source.StartActivity("call dependency")!;
        activity.SetTag("server.address", "api.example.com");
        activity.SetTag("url.full", "https://api.example.com/customers/0101901234");

        CreateProcessor("couch").OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.full"));
        Assert.Equal("api.example.com", activity.GetTagItem("server.address"));
    }

    [Fact]
    public void Keeps_url_full_on_a_span_to_a_configured_CouchDb_host()
    {
        // By this point CouchDbUrlPolicy has already replaced the document ID
        // with {docid} — the value below is what actually reaches the processor.
        using var activity = _source.StartActivity("application.load")!;
        activity.SetTag("server.address", "couch");
        activity.SetTag("url.full", "http://couch:5984/kyc/{docid}");

        CreateProcessor("couch").OnEnd(activity);

        Assert.Equal("http://couch:5984/kyc/{docid}", activity.GetTagItem("url.full"));
    }

    [Fact]
    public void Drops_url_full_when_the_CouchDb_host_is_not_configured()
    {
        // 🔒 Fails closed, opposite to CouchDbUrlPolicy's redaction, which
        // fails open. A host missing from configuration loses diagnostic value
        // here rather than leaking an unredacted URL.
        using var activity = _source.StartActivity("application.load")!;
        activity.SetTag("server.address", "couch");
        activity.SetTag("url.full", "http://couch:5984/kyc/abc-123");

        CreateProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.full"));
    }

    [Fact]
    public void Leaves_a_span_untouched_when_every_key_is_allowlisted()
    {
        using var activity = _source.StartActivity("screen application")!;
        activity.SetTag("http.request.method", "POST");
        activity.SetTag("correlation.id", "abc");

        CreateProcessor().OnEnd(activity);

        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
        Assert.Equal("abc", activity.GetTagItem("correlation.id"));
    }
}
