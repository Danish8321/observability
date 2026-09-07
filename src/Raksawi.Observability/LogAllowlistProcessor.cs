using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Raksawi.Observability;

/// <summary>
/// Drops every log attribute whose key is not allowlisted, before export
/// (ADR-0003). The log counterpart to <see cref="AllowlistProcessor"/>, and the
/// second enforcement point for the "permitted on spans and logs" half of the
/// Class 2 rule.
/// </summary>
/// <remarks>
/// <para>
/// Structured log properties are span attributes by another name: a service
/// writing <c>LogInformation("Screened {ApplicationId}", id)</c> produces an
/// attribute named <c>ApplicationId</c>, and until this existed the collector
/// was the only thing standing between it and a store (ADR-0028).
/// </para>
/// <para>
/// 🔒 <b>The message body is not filtered and cannot be.</b> This drops
/// attributes by key; a template that interpolated a value into the text has
/// already destroyed the structure any filter would need. That is the whole
/// reason ADR-0004 bans interpolated log strings — an unqueryable line is also
/// an unredactable one, and this processor is what makes that ban load-bearing
/// rather than stylistic.
/// </para>
/// <para>
/// Log records have no CouchDB conditional: <c>url.full</c> is allowed on a
/// span to a known CouchDB host because <see cref="CouchDbUrlPolicy"/> redacted
/// it first, and nothing does that for a log. So logs are evaluated with the
/// conditional off, which fails closed.
/// </para>
/// </remarks>
internal sealed class LogAllowlistProcessor : BaseProcessor<LogRecord>
{
    private readonly AttributeAllowlist _allowlist;
    private readonly AllowlistDropMetric _dropped = new(AllowlistDropMetric.LogSignal);

    public LogAllowlistProcessor(AttributeAllowlist allowlist) => _allowlist = allowlist;

    public override void OnEnd(LogRecord data)
    {
        if (data?.Attributes is null)
        {
            return;
        }

        List<KeyValuePair<string, object?>>? kept = null;

        for (var i = 0; i < data.Attributes.Count; i++)
        {
            var attribute = data.Attributes[i];

            if (_allowlist.IsAllowed(attribute.Key, isCouchDbSpan: false))
            {
                kept?.Add(attribute);
                continue;
            }

            // The first drop is what forces a copy. A record whose attributes
            // are all allowlisted — the normal case — allocates nothing.
            if (kept is null)
            {
                kept = new List<KeyValuePair<string, object?>>(data.Attributes.Count);

                for (var j = 0; j < i; j++)
                {
                    kept.Add(data.Attributes[j]);
                }
            }

            _dropped.Record(attribute.Key);
        }

        if (kept is not null)
        {
            data.Attributes = kept;
        }
    }
}
