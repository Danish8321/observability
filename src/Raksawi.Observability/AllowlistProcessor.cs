using System.Diagnostics;
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
    private readonly AttributeAllowlist _allowlist;
    private readonly IReadOnlyCollection<string> _couchDbHosts;
    private readonly AllowlistDropMetric _dropped = new(AllowlistDropMetric.SpanSignal);

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
            _dropped.Record(key);
        }
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
