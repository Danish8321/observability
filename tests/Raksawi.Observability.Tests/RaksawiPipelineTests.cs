using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The CouchDB rules were previously written inline in the .NET 10 entry point
/// and absent from the .NET Framework one. They now live in one place that both
/// runtimes call, so these cover the rule for both — what fires on 4.8 still
/// needs a real span (Phase 2, ADR-0005).
/// </summary>
public class RaksawiPipelineTests
{
    private static RaksawiObservabilityOptions Options(params string[] couchDbHosts)
    {
        var options = new RaksawiObservabilityOptions
        {
            ServiceName = "screening-api",
            ServiceNamespace = "kyc",
            SamplingRatio = 1.0,
        };

        foreach (var host in couchDbHosts)
        {
            options.CouchDbHosts.Add(host);
        }

        return options;
    }

    [Fact]
    public void A_configured_couchdb_host_is_redacted()
    {
        var redactedOk = RaksawiPipeline.TryRedactCouchDbUrl(
            Options("couch"), new Uri("http://couch:5984/kyc/app-1001"), out var redacted);

        Assert.True(redactedOk);
        Assert.Equal("http://couch:5984/kyc/{docid}", redacted);
    }

    [Fact]
    public void An_unconfigured_host_is_not_redacted_and_says_nothing()
    {
        // 🔒 Fails open, deliberately and documented (ADR-0023): a wrong host
        // list means silent non-redaction, not an error. The allowlist
        // processor fails closed in the other direction for the same key.
        var redactedOk = RaksawiPipeline.TryRedactCouchDbUrl(
            Options("couch"), new Uri("http://other:5984/kyc/app-1001"), out _);

        Assert.False(redactedOk);
    }

    [Fact]
    public void Redaction_can_be_turned_off()
    {
        var options = Options("couch");
        options.RedactCouchDbUrls = false;

        Assert.False(RaksawiPipeline.TryRedactCouchDbUrl(
            options, new Uri("http://couch:5984/kyc/app-1001"), out _));
    }

    [Fact]
    public void The_changes_feed_is_not_traced()
    {
        // A long-poll held open for minutes produces spans of arbitrary
        // duration, which corrupt every latency percentile computed from spans.
        Assert.False(RaksawiPipeline.ShouldTrace(new Uri("http://couch:5984/kyc/_changes")));
        Assert.True(RaksawiPipeline.ShouldTrace(new Uri("http://couch:5984/kyc/app-1001")));
    }

    [Fact]
    public void A_request_with_no_uri_is_traced_rather_than_dropped()
    {
        Assert.True(RaksawiPipeline.ShouldTrace(null));
    }
}
