using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using global::Npgsql;

namespace Raksawi.Observability.Npgsql;

/// <summary>
/// Postgres tracing, for a service whose database is Postgres. Opt-in per
/// database rather than in core (ADR-0042).
/// </summary>
public static class RaksawiNpgsqlExtensions
{
    /// <summary>
    /// Adds Npgsql's client spans to the provider
    /// <c>AddRaksawiObservability</c> builds. They pass the same in-process
    /// allowlist as every other span, which drops the statement text.
    /// </summary>
    public static IHostApplicationBuilder AddRaksawiNpgsql(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A source subscription, not a processor, so the allowlist processor
        // stays last before the exporter.
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddNpgsql());

        return builder;
    }
}
