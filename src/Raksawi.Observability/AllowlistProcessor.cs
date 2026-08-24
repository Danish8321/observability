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
    internal const string MeterName = "Raksawi.Observability.Allowlist";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DroppedKeys = Meter.CreateCounter<long>(
        "raksawi.telemetry.attributes.dropped",
        unit: "{attribute}",
        description: "Span attributes dropped because their key is not allowlisted.");

    private readonly AttributeAllowlist _allowlist;
    private readonly IReadOnlyCollection<string> _couchDbHosts;

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
            DroppedKeys.Add(1, new KeyValuePair<string, object?>("attribute.key", key));
        }
    }

    /// <summary>
    /// Whether this span is an outbound call to a configured CouchDB host.
    /// </summary>
    /// <remarks>
    /// 🔒 Exact host match, and it <b>fails open</b> in the same direction
    /// <see cref="CouchDbUrlPolicy"/> does: an unconfigured host is not treated
    /// as CouchDB, so <c>url.full</c> is denied rather than allowed. A host
    /// missing from configuration therefore loses diagnostic value, and never
    /// leaks — the opposite of the redaction path's failure, deliberately.
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
