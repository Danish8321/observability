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
    private readonly AllowlistDropMetric _dropped = new(AllowlistDropMetric.SpanSignal);

    public AllowlistProcessor(AttributeAllowlist allowlist)
    {
        _allowlist = allowlist;
    }

    public override void OnEnd(Activity data)
    {
        if (data is null)
        {
            return;
        }

        var redactedUrl = data.GetCustomProperty(TelemetryMarkers.UrlRedacted) as string;
        List<string>? toDrop = null;

        foreach (var tag in data.TagObjects)
        {
            if (_allowlist.IsAllowed(tag.Key, IsRedactedCouchDbUrl(tag, redactedUrl)))
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
    /// Whether this tag is the CouchDB URL our HttpClient hook redacted,
    /// unchanged since.
    /// </summary>
    /// <remarks>
    /// 🔒 C5: the record is the value written, not a flag, so anything that
    /// rewrites <c>url.full</c> afterwards makes the key drop rather than leak.
    /// Fails closed: a span without the record — redaction off, an
    /// unconfigured host, another library — never exports the URL.
    /// <c>url.query</c> has no record yet, so it always drops.
    /// </remarks>
    private static bool IsRedactedCouchDbUrl(KeyValuePair<string, object?> tag, string? redactedUrl) =>
        redactedUrl is not null
        && string.Equals(tag.Key, "url.full", StringComparison.Ordinal)
        && string.Equals(tag.Value as string, redactedUrl, StringComparison.Ordinal);
}
