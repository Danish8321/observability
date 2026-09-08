using System.Diagnostics.Metrics;

namespace Raksawi.Observability;

/// <summary>
/// Whether this process had to have its trace-context format corrected at
/// startup, as a metric rather than only a log line.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0005 pairs the startup warning with a metric, on the grounds that "a
/// warning in a log on an IIS host is not a control" — the runtime where the
/// failure is expected is also the runtime least likely to have anyone reading
/// its logs. This is that metric, and it is what panel 4.10 of
/// <c>docs/diagnostic-queries.md</c> reads.
/// </para>
/// <para>
/// 🔒 An observable gauge rather than the counter ADR-0005's wording implies,
/// for two reasons. A counter would have to be incremented during
/// <see cref="ServiceIdentity.EnsureW3CTraceContext"/>, which both entry points
/// call <i>before</i> the meter provider exists — the measurement would be
/// taken with nothing subscribed and reach no store, which is the silent
/// failure this metric exists to expose, reproduced inside the fix. A gauge is
/// read at collection time instead, so the ordering cannot break it. It also
/// answers the dashboard question directly: the state holds for the process
/// lifetime, so the panel is correct in any time window rather than only in the
/// one containing startup.
/// </para>
/// <para>
/// Reported as 0 by healthy processes rather than not reported at all. "The
/// check ran and the format was already W3C" and "nothing is reporting" are
/// different answers, and a panel that cannot tell them apart is the same
/// silent gap in a different place.
/// </para>
/// </remarks>
internal static class TraceContextMetric
{
    /// <summary>
    /// Registered on the meter provider by <c>AddRaksawiIdentity</c>. A meter
    /// nothing subscribes to is collected by nothing.
    /// </summary>
    internal const string MeterName = "Raksawi.Observability.TraceContext";

    private static readonly Meter Meter = new(MeterName);

    private static long _corrected;

    /// <summary>
    /// Created in the static constructor rather than held in a field: the
    /// <see cref="Meter"/> owns its published instruments, so nothing needs to
    /// reference this one again, and an unread private field does not survive
    /// this repository's warnings-as-errors build.
    /// </summary>
    static TraceContextMetric() =>
        _ = Meter.CreateObservableGauge(
            "raksawi.telemetry.trace_context.corrected",
            () => Interlocked.Read(ref _corrected),
            unit: "{process}",
            description:
                "1 when this process started with a non-W3C trace context format " +
                "that had to be corrected, 0 when it was already correct.");

    /// <summary>
    /// Records the outcome of the startup check. Called unconditionally, so the
    /// static constructor above runs — and therefore the instrument is
    /// published — on every start rather than only on a broken one.
    /// </summary>
    internal static void Record(bool corrected) =>
        Interlocked.Exchange(ref _corrected, corrected ? 1 : 0);

    /// <summary>The value the gauge would report now.</summary>
    internal static long Current => Interlocked.Read(ref _corrected);
}
