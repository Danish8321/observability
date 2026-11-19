using Microsoft.CodeAnalysis;

namespace Raksawi.Observability.Analyzers;

/// <summary>
/// The diagnostics this package raises. Each is a review trigger, not a wall:
/// Rev 3 D2.6 holds that a bypassed abstraction is worse than none, so raw
/// <c>Activity</c> and <c>SetTag</c> stay available for genuine one-offs and
/// the diagnostic is what puts the one-off in front of a reviewer.
/// </summary>
internal static class Diagnostics
{
    private const string GovernanceCategory = "Raksawi.Governance";

    /// <summary>
    /// A tag key nobody declared. Warning rather than error, deliberately —
    /// see the class remarks. The runtime allowlist drops it either way
    /// (ADR-0003), so this exists to make that drop visible at build time
    /// rather than as missing telemetry during an incident.
    /// </summary>
    public static readonly DiagnosticDescriptor UndeclaredAttributeKey = new(
        id: "RKS001",
        title: "Attribute key is not allowlisted",
        messageFormat: "Attribute key '{0}' is not allowlisted and will be dropped before export. Declare it with [assembly: AllowedAttributeKey] in a policy pack, or use a key inside an allowed family.",
        category: GovernanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ADR-0003 drops any attribute whose key is not allowlisted. A key flagged here reaches no store, so the failure is silent unless it is caught at build.");

    /// <summary>
    /// 🔒 Class 2 as a metric dimension. Error, not warning: this one is not a
    /// judgement call.
    /// </summary>
    /// <remarks>
    /// Metrics are pre-aggregated per unique dimension combination, so an
    /// unbounded dimension produces unbounded series and degrades the store for
    /// every other service too. Rev 3 D2.1 rule 1. This is a cost and
    /// availability rule rather than a privacy one, which is why it is absolute
    /// even though Class 2 identifiers are opaque.
    /// </remarks>
    public static readonly DiagnosticDescriptor ClassTwoAsMetricDimension = new(
        id: "RKS002",
        title: "Opaque business identifier used as a metric dimension",
        messageFormat: "'{0}' is a Class 2 identifier and must never be a metric dimension. Count without it and use the trace store to answer 'which ones'.",
        category: GovernanceCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Rev 3 D2.1 rule 1. A per-identifier dimension is a memory leak with a dashboard: metrics pre-aggregate per unique dimension combination, so an unbounded dimension produces unbounded series.");

    /// <summary>
    /// Exporter assembled by hand, outside the library. This is where
    /// per-service divergence enters (ADR-0001), and it also bypasses the
    /// allowlist processor, which is the only source-side control there is.
    /// </summary>
    public static readonly DiagnosticDescriptor ExporterConfiguredDirectly = new(
        id: "RKS003",
        title: "OTLP exporter configured outside the observability library",
        messageFormat: "Configure telemetry through AddRaksawiObservability (or RaksawiObservability.Start on .NET Framework), not by calling '{0}' directly. A hand-assembled pipeline has no allowlist processor and no governed defaults.",
        category: GovernanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ADR-0001: a service adopting this platform should not be assembling exporters and instrumentation by hand. A pipeline built directly skips the ADR-0003 allowlist entirely.");

    /// <summary>
    /// A histogram in seconds with no bucket advice gets the SDK's default
    /// boundaries, which are milliseconds. Every value under five seconds falls
    /// in the first bucket, so its percentiles are fiction.
    /// </summary>
    public static readonly DiagnosticDescriptor SecondsHistogramWithoutAdvice = new(
        id: "RKS004",
        title: "Histogram in seconds without bucket advice",
        messageFormat: "Histogram '{0}' has unit \"s\" but no bucket advice, so it gets the SDK's millisecond boundaries (0, 5, 10 … 10000) and its percentiles are meaningless. Pass advice: new InstrumentAdvice<T> {{ HistogramBucketBoundaries = [...] }}.",
        category: GovernanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Found building the Screening slow alert: screening.duration in seconds arrived with millisecond buckets and p95 read 4.75 s whatever the real duration. ASP.NET Core's and HttpClient's own duration histograms declare advice; a service's own must too.");
}
