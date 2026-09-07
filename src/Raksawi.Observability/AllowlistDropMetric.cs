using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Raksawi.Observability;

/// <summary>
/// The dropped-attribute counter, shared by the span and log allowlist
/// processors.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0003: dropped keys are counted, dimensioned by attribute key. Without
/// this, a family that failed to cover a real key is indistinguishable from
/// instrumentation that is not running — the failure mode is silent by
/// construction, so the counter is what makes it answerable from a dashboard
/// rather than by reading library source.
/// </para>
/// <para>
/// One counter across both signals, with <c>telemetry.signal</c> telling them
/// apart. Two counters would make "is anything being dropped" two questions,
/// and the dimension is bounded at two values forever.
/// </para>
/// </remarks>
internal sealed class AllowlistDropMetric
{
    /// <summary>
    /// Registered on the meter provider by the entry points. A meter nothing
    /// subscribes to collects nothing, which would leave this counter
    /// incrementing in-process and reaching no store — the same silent failure
    /// it exists to expose.
    /// </summary>
    internal const string MeterName = "Raksawi.Observability.Allowlist";

    /// <summary>
    /// 🔒 The key dimension is bounded. Dropped keys are by definition the keys
    /// no family covers and no pack declared, so a service building keys from
    /// data (<c>$"app.{id}"</c>) would otherwise produce one series per value —
    /// the unbounded metric dimension RKS002 raises as an <b>error</b>, in the
    /// component that enforces it.
    /// </summary>
    /// <remarks>
    /// A family prefix was rejected as the bound: a key whose <i>first</i>
    /// segment is the varying part is still unbounded after truncation. Capping
    /// the distinct set is bounded whatever the key looks like, and keeps the
    /// full key in the normal case, which is a handful of undeclared keys.
    /// </remarks>
    internal const int MaxDistinctDroppedKeys = 100;

    /// <summary>The dimension value used once <see cref="MaxDistinctDroppedKeys"/> is reached.</summary>
    internal const string OverflowDimension = "{other}";

    /// <summary>Values of the <c>telemetry.signal</c> dimension.</summary>
    internal const string SpanSignal = "span";

    /// <summary>Values of the <c>telemetry.signal</c> dimension.</summary>
    internal const string LogSignal = "log";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DroppedKeys = Meter.CreateCounter<long>(
        "raksawi.telemetry.attributes.dropped",
        unit: "{attribute}",
        description: "Attributes dropped because their key is not allowlisted.");

    /// <summary>
    /// Keys already admitted as dimension values. Per-instance rather than
    /// static: a provider has one processor per signal, so the bound holds, and
    /// a test can reach the cap without depending on what other tests dropped
    /// first.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _reportedKeys = new(StringComparer.Ordinal);

    private readonly string _signal;

    internal AllowlistDropMetric(string signal) => _signal = signal;

    /// <summary>Counts one dropped attribute.</summary>
    internal void Record(string key) =>
        DroppedKeys.Add(
            1,
            new KeyValuePair<string, object?>("attribute.key", DimensionFor(key)),
            new KeyValuePair<string, object?>("telemetry.signal", _signal));

    /// <summary>
    /// The dimension value for <paramref name="key"/>, or
    /// <see cref="OverflowDimension"/> once the cap is reached.
    /// </summary>
    /// <remarks>
    /// The check-then-add is not atomic, so concurrent first sightings can
    /// admit a few keys beyond the cap. That is a bound of cap + concurrency,
    /// not an unbounded set, and paying for a lock on the export path to make
    /// the number exact buys nothing.
    /// </remarks>
    private string DimensionFor(string key)
    {
        if (_reportedKeys.ContainsKey(key))
        {
            return key;
        }

        if (_reportedKeys.Count >= MaxDistinctDroppedKeys)
        {
            return OverflowDimension;
        }

        _ = _reportedKeys.TryAdd(key, 0);
        return key;
    }
}
