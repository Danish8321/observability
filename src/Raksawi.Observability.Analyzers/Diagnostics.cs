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
}
