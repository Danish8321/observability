#if NET10_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
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

        var w3cWarning = ServiceIdentity.EnsureW3CTraceContext();
        if (w3cWarning is not null)
        {
            builder.Logging.AddFilter("Raksawi.Observability", LogLevel.Warning);
        }

        var resource = ServiceIdentity.BuildResource(options);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resource)
                .SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(options.EffectiveSamplingRatio)))
                .AddSource(RaksawiObservabilityOptions.NatsActivitySourceName)
                .AddSource(options.ActivitySources.ToArray())
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation(http =>
                {
                    http.FilterHttpRequestMessage = request =>
                        request.RequestUri is null || !CouchDbUrlPolicy.IsChangesFeed(request.RequestUri);

                    http.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        if (!options.RedactCouchDbUrls || request.RequestUri is null)
                        {
                            return;
                        }

                        if (options.CouchDbHosts.Contains(request.RequestUri.Host))
                        {
                            activity.SetTag("url.full", CouchDbUrlPolicy.Redact(request.RequestUri));
                        }
                    };
                })
                .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/traces")))
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(otlp => ConfigureOtlp(otlp, options, "v1/metrics")));

        return builder;
    }

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
#endif
