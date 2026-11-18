using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace Raksawi.Observability.SqlClient;

/// <summary>
/// SQL Server tracing, for a service whose database is SQL Server. Opt-in per
/// database rather than in core (ADR-0042): the estate runs SQL Server,
/// Postgres and CouchDB, and core registering one would load it into services
/// on the others.
/// </summary>
public static class RaksawiSqlClientExtensions
{
    /// <summary>
    /// Adds SQL Server client spans to the provider
    /// <c>AddRaksawiObservability</c> builds. Governance needs nothing here:
    /// the spans pass the same in-process allowlist as every other span, which
    /// drops the statement text (<c>db.query.text</c>, a Class 3 carve-out).
    /// </summary>
    public static IHostApplicationBuilder AddRaksawiSqlClient(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Instrumentation, not a processor, so registering it after core does
        // not move the allowlist processor off its place before the exporter.
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSqlClientInstrumentation());

        return builder;
    }
}
