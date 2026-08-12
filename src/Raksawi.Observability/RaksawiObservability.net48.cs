#if NETFRAMEWORK
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Raksawi.Observability;

/// <summary>
/// .NET Framework 4.8 entry point. Called from Application_Start; the returned
/// handle is disposed in Application_End.
/// </summary>
/// <remarks>
/// <para>
/// Built from the first commit per ADR-0012 and <b>unvalidated</b> until Phase 2
/// per ADR-0005 — the three documented 4.8 failure modes are reproduced against
/// a fixture before this path is trusted. ADR-0022 defers that past the demo.
/// </para>
/// <para>
/// This does not wire TelemetryHttpModule, which must be registered in
/// Web.config, and IIS must run in integrated pipeline mode.
/// </para>
/// </remarks>
public static class RaksawiObservability
{
    /// <summary>
    /// Starts telemetry. Never throws for telemetry reasons: a service must be
    /// able to start without a collector (Rev 3 I3.6).
    /// </summary>
    public static IDisposable Start(Action<RaksawiObservabilityOptions> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new RaksawiObservabilityOptions();
        configure(options);
        options.Validate();

        // On .NET Framework the default is Hierarchical, and the failure is
        // silent: a trace splits in two rather than erroring. This is the
        // single most important line in the 4.8 path.
        var w3cWarning = ServiceIdentity.EnsureW3CTraceContext();

        var resource = ServiceIdentity.BuildResource(options);

        var tracer = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(options.EffectiveSamplingRatio)))
            .AddSource(RaksawiObservabilityOptions.NatsActivitySourceName)
            .AddSource(options.ActivitySources.ToArray())
            .AddAspNetInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/traces"))
            .Build();

        var meter = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/metrics"))
            .Build();

        return new Handle(tracer, meter, w3cWarning);
    }

    private static void ConfigureOtlp(OtlpExporterOptions otlp, RaksawiObservabilityOptions options, string signalPath)
    {
        // gRPC is not supported on .NET Framework. This is not a preference.
        //
        // The SDK only auto-appends the per-signal path (v1/traces,
        // v1/metrics) when Endpoint comes from its own default or from
        // OTEL_EXPORTER_OTLP_ENDPOINT. Setting Endpoint programmatically, as
        // this always does, opts out of that — so the path is appended here
        // explicitly, or every export 404s against the bare endpoint.
        otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
        otlp.Endpoint = new Uri(options.OtlpEndpoint, signalPath);
    }

    private sealed class Handle : IDisposable
    {
        private readonly TracerProvider _tracer;
        private readonly MeterProvider _meter;

        public Handle(TracerProvider tracer, MeterProvider meter, string? w3cWarning)
        {
            _tracer = tracer;
            _meter = meter;
            W3CWarning = w3cWarning;
        }

        /// <summary>
        /// Non-null when the trace context format had to be corrected. Surfaced
        /// rather than logged, because there is no logging abstraction to
        /// assume on this runtime.
        /// </summary>
        public string? W3CWarning { get; }

        public void Dispose()
        {
            _tracer.Dispose();
            _meter.Dispose();
        }
    }
}
#endif
