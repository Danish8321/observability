using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Screening.Domain;

/// <summary>
/// This service's own instrumentation. One ActivitySource and one Meter per
/// service, declared once and injected nowhere — they are process-wide by
/// design, and creating them per-request is the most common way to leak.
/// </summary>
/// <remarks>
/// <para>
/// The names are the ones registered in <c>ActivitySources</c> when calling
/// <c>AddRaksawiObservability</c>. A source that is not registered emits
/// nothing, silently, which is the second most common wiring mistake after
/// forgetting the collector endpoint.
/// </para>
/// <para>
/// <b>Version matters.</b> It is exported as <c>otel.scope.version</c> and is
/// how you tell "this span is missing" from "this service is running an old
/// build that never emitted it".
/// </para>
/// </remarks>
public static class ScreeningTelemetry
{
    public const string ActivitySourceName = "Raksawi.Screening";
    public const string MeterName = "Raksawi.Screening";

    public static readonly ActivitySource Source = new(ActivitySourceName, "1.0.0");

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Applications screened, by outcome.
    /// </summary>
    /// <remarks>
    /// 🔒 Dimensioned by <c>outcome</c> and nothing else. Adding
    /// <c>application.id</c> here would be a Class 2 identifier as a metric
    /// dimension — unbounded cardinality, and the fastest way to make a metrics
    /// store unusable. The trace store answers "which applications"; this
    /// answers "how many".
    /// </remarks>
    public static readonly Counter<long> Screened =
        Meter.CreateCounter<long>(
            "screening.applications.screened",
            unit: "{application}",
            description: "Applications screened, by outcome.");

    /// <summary>
    /// How long screening took, from message receipt to decision.
    /// </summary>
    /// <remarks>
    /// A histogram rather than a gauge: the question at 3am is "what does the
    /// slow tail look like", and an average cannot answer it.
    /// </remarks>
    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(
            "screening.duration",
            unit: "s",
            description: "Screening duration from message receipt to decision.");

    /// <summary>
    /// Messages that failed and were not retried further.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Screened"/>. A failure is not an
    /// outcome of screening, it is an absence of one, and merging them hides
    /// the case where work silently never completed.
    /// </remarks>
    public static readonly Counter<long> Abandoned =
        Meter.CreateCounter<long>(
            "screening.applications.abandoned",
            unit: "{application}",
            description: "Applications whose screening failed permanently.");
}
