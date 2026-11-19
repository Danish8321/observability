using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Raksawi.Observability;

/// <summary>
/// Filters log attributes before export, by the allowlist for a service that
/// set <see cref="RaksawiObservabilityOptions.GovernanceRequired"/> and by the
/// denylist otherwise (ADR-0043). The log counterpart to
/// <see cref="AllowlistProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The denylist is the estate's (<see cref="AllowlistRules.IsDeniedOnLog"/>)
/// plus the service's own <see cref="RaksawiObservabilityOptions.DeniedLogKeys"/>.
/// A vocabulary of every field before the first useful log line was what
/// stopped services adopting this, and most fields are harmless.
/// </para>
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
    private readonly Func<string, bool> _keeps;
    private readonly AllowlistDropMetric _dropped = new(AllowlistDropMetric.LogSignal);

    /// <summary>Allowlist mode, for a service that opted into governance.</summary>
    public LogAllowlistProcessor(AttributeAllowlist allowlist) =>
        _keeps = key => allowlist.IsAllowed(key, isCouchDbSpan: false);

    private LogAllowlistProcessor(Func<string, bool> keeps) => _keeps = keeps;

    /// <summary>
    /// Denylist mode: everything except the estate denylist and
    /// <paramref name="serviceDeniedKeys"/>, compared in canonical form.
    /// </summary>
    internal static LogAllowlistProcessor ForDenylist(IEnumerable<string> serviceDeniedKeys)
    {
        var denied = new HashSet<string>(
            serviceDeniedKeys.Select(AllowlistRules.CanonicalLogName), StringComparer.Ordinal);

        return new(key => !AllowlistRules.IsDeniedOnLog(key)
            && !denied.Contains(AllowlistRules.CanonicalLogName(key)));
    }

    /// <summary>The mode <paramref name="options"/> asks for.</summary>
    internal static LogAllowlistProcessor For(RaksawiObservabilityOptions options) =>
        options.GovernanceRequired
            ? new LogAllowlistProcessor(AttributeAllowlist.FromLoadedAssemblies())
            : ForDenylist(options.DeniedLogKeys);

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

            if (_keeps(attribute.Key))
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
