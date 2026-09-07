#if NET10_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Raksawi.Observability;

/// <summary>
/// .NET 10 entry point. One call, per ADR-0001 — a service adopting this should
/// not be assembling exporters and instrumentation by hand, because that is
/// exactly where per-service divergence enters.
/// </summary>
public static class RaksawiObservabilityExtensions
{
    /// <summary>
    /// Registers tracing and metrics for this service. Never fails a service
    /// start for telemetry reasons (Rev 3 I3.6), but does fail fast on
    /// misconfiguration — an absent service name or sampler is a configuration
    /// error, not a telemetry outage.
    /// </summary>
    public static IHostApplicationBuilder AddRaksawiObservability(
        this IHostApplicationBuilder builder,
        Action<RaksawiObservabilityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RaksawiObservabilityOptions
        {
            IsDevelopment = builder.Environment.IsDevelopment(),
        };
        configure(options);
        options.Validate();

        // No logger exists yet — the host is not built — so the warning is
        // handed to a hosted service that writes it once at start. It was
        // previously used to raise a log filter and then discarded, which
        // logged nothing and changed the level for unrelated categories.
        if (ServiceIdentity.EnsureW3CTraceContext() is not null)
        {
            builder.Services.AddHostedService<W3CTraceContextWarning>();
        }

        var resource = ServiceIdentity.BuildResource(options);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddRaksawiIdentity(options, resource)
                // Between identity and export: everything here is specific to
                // this runtime. The 4.8 entry point registers its own
                // equivalents in the same position.
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation(http =>
                {
                    http.FilterHttpRequestMessage = request =>
                        RaksawiPipeline.ShouldTrace(request.RequestUri);

                    http.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        if (RaksawiPipeline.TryRedactCouchDbUrl(options, request.RequestUri, out var redacted))
                        {
                            activity.SetTag("url.full", redacted);
                        }
                    };
                })
                .AddRaksawiExport(options))
            .WithMetrics(metrics => metrics
                .AddRaksawiIdentity(options, resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddRaksawiExport(options))
            // 🔒 Logs go through the same allowlist as spans (ADR-0028).
            // Structured properties are span attributes by another name, and
            // before this they reached the collector unfiltered while ADR-0003
            // claimed an in-process control for every signal.
            .WithLogging(logging => logging.AddRaksawiLogging(options, resource));

        return builder;
    }
}
#endif
