#if NETFRAMEWORK
using OpenTelemetry;
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
    /// <returns>
    /// The handle to hold for the application lifetime and dispose in
    /// <c>Application_End</c>. Read
    /// <see cref="RaksawiObservabilityHandle.W3CWarning"/> from it — a non-null
    /// value there is the one 4.8 failure that is otherwise silent.
    /// </returns>
    public static RaksawiObservabilityHandle Start(Action<RaksawiObservabilityOptions> configure)
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
            .AddRaksawiIdentity(options, resource)
            // Between identity and export: everything here is specific to this
            // runtime. The .NET 10 entry point registers its own equivalents in
            // the same position.
            .AddAspNetInstrumentation()
            .AddHttpClientInstrumentation(http =>
            {
                // 🔒 The same two CouchDB rules the .NET 10 path applies,
                // reached through this runtime's hooks: HttpClient on .NET
                // Framework is instrumented at HttpWebRequest, so the
                // HttpRequestMessage callbacks never fire here. Wiring nothing
                // left document identifiers in url.full on 4.8 while the
                // allowlist's conditional carve-out went on admitting the key.
                http.FilterHttpWebRequest = request =>
                    RaksawiPipeline.ShouldTrace(request?.RequestUri);

                http.EnrichWithHttpWebRequest = (activity, request) =>
                {
                    if (RaksawiPipeline.TryRedactCouchDbUrl(options, request?.RequestUri, out var redacted))
                    {
                        activity.SetTag("url.full", redacted);
                    }
                };
            })
            .AddRaksawiExport(options)
            .Build();

        var meter = Sdk.CreateMeterProviderBuilder()
            .AddRaksawiIdentity(options, resource)
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddRaksawiExport(options)
            .Build();

        return new RaksawiObservabilityHandle(tracer, meter, w3cWarning);
    }

}

/// <summary>
/// The live telemetry providers on .NET Framework 4.8. Held for the
/// application lifetime and disposed in <c>Application_End</c>.
/// </summary>
/// <remarks>
/// Public, and not just an <see cref="IDisposable"/>, because
/// <see cref="W3CWarning"/> has to be readable. It was previously a property on
/// a private nested class returned as <see cref="IDisposable"/>, so no caller
/// could reach the diagnostic for the failure this runtime is most likely to
/// hit.
/// </remarks>
public sealed class RaksawiObservabilityHandle : IDisposable
{
    private readonly TracerProvider _tracer;
    private readonly MeterProvider _meter;

    internal RaksawiObservabilityHandle(TracerProvider tracer, MeterProvider meter, string? w3cWarning)
    {
        _tracer = tracer;
        _meter = meter;
        W3CWarning = w3cWarning;
    }

    /// <summary>
    /// Non-null when the trace context format had to be corrected at startup.
    /// </summary>
    /// <remarks>
    /// 🔒 Surfaced rather than logged: there is no logging abstraction to assume
    /// on this runtime. Write it wherever the application already writes
    /// startup diagnostics. On .NET Framework the default id format is
    /// <c>Hierarchical</c> and the failure is silent — a trace splits in two
    /// rather than erroring — so this is the only signal that it happened, and
    /// if it appears after an <c>Activity</c> already exists, traces have
    /// already split.
    /// </remarks>
    public string? W3CWarning { get; }

    /// <summary>Shuts the providers down, flushing what is pending.</summary>
    public void Dispose()
    {
        _tracer.Dispose();
        _meter.Dispose();
    }
}
#endif
