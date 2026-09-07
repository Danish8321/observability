using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;

namespace Raksawi.Observability;

/// <summary>
/// Drops every span attribute whose key is not allowlisted, before export
/// (ADR-0003). The last thing that runs in-process, and the only source-side
/// control that sees attributes it did not originate — including those set by
/// third-party instrumentation packages, which the analyzer cannot see at all.
/// </summary>
internal sealed class AllowlistProcessor : BaseProcessor<Activity>
{
    /// <summary>
    /// ADR-0003: dropped keys are counted, dimensioned by attribute key.
    /// Without this, a family that failed to cover a real key is
    /// indistinguishable from instrumentation that is not running — the
    /// failure mode is silent by construction, so the counter is what makes it
    /// answerable from a dashboard rather than by reading library source.
    /// </summary>
    /// <remarks>
    /// Registered on the meter provider by the entry points. A meter nothing
    /// subscribes to collects nothing, which would leave this counter
    /// incrementing in-process and reaching no store — the same silent failure
    /// it exists to expose.
    /// </remarks>
    internal const string MeterName = "Raksawi.Observability.Allowlist";

    /// <summary>
    /// 🔒 The dimension is bounded. Dropped keys are by definition the keys no
    /// family covers and no pack declared, so a service building keys from data
    /// (<c>$"app.{id}"</c>) would otherwise produce one series per value — the
    /// unbounded metric dimension RKS002 raises as an <b>error</b>, in the
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

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DroppedKeys = Meter.CreateCounter<long>(
        "raksawi.telemetry.attributes.dropped",
        unit: "{attribute}",
        description: "Span attributes dropped because their key is not allowlisted.");

    private readonly AttributeAllowlist _allowlist;
    private readonly IReadOnlyCollection<string> _couchDbHosts;

    /// <summary>
    /// Keys already admitted as dimension values. Per-processor rather than
    /// static: a provider has one processor, so the bound holds, and a test can
    /// reach the cap without depending on what other tests dropped first.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _reportedKeys = new(StringComparer.Ordinal);

    public AllowlistProcessor(AttributeAllowlist allowlist, IReadOnlyCollection<string> couchDbHosts)
    {
        _allowlist = allowlist;
        _couchDbHosts = couchDbHosts;
    }

    public override void OnEnd(Activity data)
    {
        if (data is null)
        {
            return;
        }

        var isCouchDbSpan = IsCouchDbSpan(data);
        List<string>? toDrop = null;

        foreach (var tag in data.TagObjects)
        {
            if (_allowlist.IsAllowed(tag.Key, isCouchDbSpan))
            {
                continue;
            }

            // Collected rather than removed in place: SetTag mutates the same
            // collection this loop is walking.
            (toDrop ??= []).Add(tag.Key);
        }

        if (toDrop is null)
        {
            return;
        }

        foreach (var key in toDrop)
        {
            _ = data.SetTag(key, null);
            DroppedKeys.Add(1, new KeyValuePair<string, object?>("attribute.key", DimensionFor(key)));
        }
    }

    /// <summary>
    /// The dimension value for <paramref name="key"/>, or
    /// <see cref="OverflowDimension"/> once the cap is reached.
    /// </summary>
    /// <remarks>
    /// The check-then-add is not atomic, so concurrent first sightings can
    /// admit a few keys beyond the cap. That is a bound of
    /// cap + concurrency, not an unbounded set, and paying for a lock on the
    /// export path to make the number exact buys nothing.
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

    /// <summary>
    /// Whether this span is an outbound call to a configured CouchDB host.
    /// </summary>
    /// <remarks>
    /// 🔒 Exact host match, failing <b>closed</b>: a host missing from
    /// configuration is not treated as CouchDB, so <c>url.full</c> is denied
    /// rather than allowed. Such a host loses diagnostic value and never leaks.
    /// <para>
    /// This is the opposite direction to <see cref="CouchDbUrlPolicy"/>'s
    /// redaction, which fails <b>open</b> on a host mismatch — that one is the
    /// path to verify against a real span.
    /// </para>
    /// </remarks>
    private bool IsCouchDbSpan(Activity data)
    {
        if (_couchDbHosts.Count == 0)
        {
            return false;
        }

        foreach (var tag in data.TagObjects)
        {
            if (!string.Equals(tag.Key, "server.address", StringComparison.Ordinal))
            {
                continue;
            }

            var host = tag.Value as string;
            if (host is null)
            {
                return false;
            }

            foreach (var couchDbHost in _couchDbHosts)
            {
                if (string.Equals(host, couchDbHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }
}
