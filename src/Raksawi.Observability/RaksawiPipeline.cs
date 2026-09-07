using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Raksawi.Observability;

/// <summary>
/// The parts of the pipeline that are the same on both runtimes, expressed
/// once.
/// </summary>
/// <remarks>
/// <para>
/// The two entry points previously built this independently — sampler, sources,
/// allowlist processor and OTLP configuration duplicated line for line — so
/// every governance change had to be applied twice by hand with nothing
/// checking the second application. The .NET Framework copy is the one deferred
/// past the demo (ADR-0022) and therefore the one that would silently fall
/// behind.
/// </para>
/// <para>
/// Split into <c>AddRaksawiIdentity</c> and
/// <c>AddRaksawiExport</c> rather than one method, because the
/// runtime-specific instrumentation has to be registered <b>between</b> them:
/// the allowlist processor must be the last thing before the exporter, and that
/// ordering is the constraint most worth keeping visible at each call site.
/// </para>
/// </remarks>
internal static class RaksawiPipeline
{
    /// <summary>
    /// Resource, sampler and the activity sources. Everything before the
    /// runtime's own instrumentation.
    /// </summary>
    internal static TracerProviderBuilder AddRaksawiIdentity(
        this TracerProviderBuilder builder,
        RaksawiObservabilityOptions options,
        ResourceBuilder resource) =>
        builder
            .SetResourceBuilder(resource)
            .SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(options.EffectiveSamplingRatio)))
            .AddSource(RaksawiObservabilityOptions.NatsActivitySourceName)
            .AddSource(options.ActivitySources.ToArray());

    /// <summary>
    /// The allowlist processor and the exporter, in that order.
    /// </summary>
    /// <remarks>
    /// 🔒 The processor is last before the exporter deliberately: it must see
    /// every attribute anything else set, including third-party instrumentation
    /// the analyzer cannot see at all (ADR-0003). Call this after the runtime's
    /// instrumentation, never before.
    /// </remarks>
    internal static TracerProviderBuilder AddRaksawiExport(
        this TracerProviderBuilder builder,
        RaksawiObservabilityOptions options) =>
        builder
            .AddProcessor(new AllowlistProcessor(
                AttributeAllowlist.FromLoadedAssemblies(),
                options.CouchDbHosts.ToArray()))
            .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/traces"));

    /// <summary>
    /// Resource and the meters this process may emit.
    /// </summary>
    /// <remarks>
    /// A meter nothing subscribes to is collected by nothing. Both the
    /// allowlist's own dropped-key counter and the service's instruments need
    /// registering, or they increment in-process and reach no store.
    /// </remarks>
    internal static MeterProviderBuilder AddRaksawiIdentity(
        this MeterProviderBuilder builder,
        RaksawiObservabilityOptions options,
        ResourceBuilder resource) =>
        builder
            .SetResourceBuilder(resource)
            .AddMeter(AllowlistDropMetric.MeterName)
            .AddMeter(options.Meters.ToArray());

    /// <summary>
    /// Resource and the log allowlist processor, then the exporter.
    /// </summary>
    /// <remarks>
    /// 🔒 One method rather than the identity/export pair the other two signals
    /// get, because there is no runtime-specific log instrumentation to slot
    /// between them — the ordering constraint that split those has nothing to
    /// protect here.
    /// <para>
    /// A log record carries structured properties, and until ADR-0028 the
    /// collector was the only thing filtering them. That made ADR-0003's middle
    /// enforcement point a claim that held for spans and not for logs, while
    /// <c>DataClass</c> said Class 2 was "permitted on spans and logs".
    /// </para>
    /// </remarks>
    internal static LoggerProviderBuilder AddRaksawiLogging(
        this LoggerProviderBuilder builder,
        RaksawiObservabilityOptions options,
        ResourceBuilder resource) =>
        builder
            .SetResourceBuilder(resource)
            .AddProcessor(new LogAllowlistProcessor(AttributeAllowlist.FromLoadedAssemblies()))
            .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/logs"));

    /// <summary>The metric exporter. There is no allowlist processor for metrics.</summary>
    internal static MeterProviderBuilder AddRaksawiExport(
        this MeterProviderBuilder builder,
        RaksawiObservabilityOptions options) =>
        builder.AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/metrics"));

    /// <summary>
    /// Whether an outbound request is one this service should redact as CouchDB,
    /// and the redacted form if so.
    /// </summary>
    /// <remarks>
    /// 🔒 Exact host match, failing <b>open</b>: a host missing from
    /// <see cref="RaksawiObservabilityOptions.CouchDbHosts"/> is not redacted,
    /// and no error says so. Verify against a real span (ADR-0023). Shared so
    /// both runtimes apply the same rule to the same URLs — they reach it
    /// through different instrumentation hooks.
    /// </remarks>
    internal static bool TryRedactCouchDbUrl(RaksawiObservabilityOptions options, Uri? uri, out string redacted)
    {
        redacted = string.Empty;

        if (!options.RedactCouchDbUrls || uri is null || !options.CouchDbHosts.Contains(uri.Host))
        {
            return false;
        }

        redacted = CouchDbUrlPolicy.Redact(uri);
        return true;
    }

    /// <summary>
    /// Whether this request is the CouchDB changes feed, which must not be
    /// traced at all.
    /// </summary>
    /// <remarks>
    /// The continuous feed is a long-poll held open for minutes. Traced as an
    /// ordinary client span it produces spans of arbitrary duration that corrupt
    /// every latency percentile computed from span data.
    /// </remarks>
    internal static bool ShouldTrace(Uri? uri) => uri is null || !CouchDbUrlPolicy.IsChangesFeed(uri);

    private static void ConfigureOtlp(OtlpExporterOptions otlp, RaksawiObservabilityOptions options, string signalPath)
    {
        // http/protobuf, not gRPC: 4317 is closed estate-wide and gRPC is
        // unsupported on the .NET Framework target this package also serves.
        //
        // The SDK only auto-appends the per-signal path (v1/traces,
        // v1/metrics) when Endpoint comes from its own default or from
        // OTEL_EXPORTER_OTLP_ENDPOINT. Setting Endpoint programmatically, as
        // this always does, opts out of that — so the path is appended here
        // explicitly, or every export 404s against the bare endpoint.
        otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
        otlp.Endpoint = new Uri(options.OtlpEndpoint, signalPath);
    }
}
