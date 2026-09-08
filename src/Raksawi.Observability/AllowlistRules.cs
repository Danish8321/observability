namespace Raksawi.Observability;

/// <summary>
/// The families and carve-outs of docs/allowlist.md, expressed once.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 This file is compiled into <b>both</b> the mechanism package and the
/// analyzer package (as a linked source item, since an analyzer must target
/// netstandard2.0 and cannot reference the library). One table, two readers —
/// the same reasoning ADR-0017 used to delete the manifest. If these rules
/// existed twice, build-time and run-time enforcement could disagree, and the
/// disagreement would be silent in exactly the direction that matters.
/// </para>
/// <para>
/// Pure string logic on purpose: no reflection, no Roslyn types, nothing that
/// would stop either side compiling it.
/// </para>
/// </remarks>
internal static class AllowlistRules
{
    /// <summary>
    /// Families allowed by prefix. Stable families need no attention on a
    /// semconv upgrade; that is the point of allowing by prefix rather than
    /// enumerating several hundred keys.
    /// </summary>
    internal static readonly string[] AllowedFamilies =
    [
        "service.", "deployment.", "vcs.", "cicd.",
        "telemetry.sdk.", "process.", "host.",
        "http.", "url.", "server.", "client.", "network.",
        "db.", "code.", "exception.", "user_agent.",
    ];

    /// <summary>
    /// 🔒 <c>messaging.*</c> is experimental and is admitted key by key, never
    /// by prefix — the stability policy in <c>docs/allowlist.md</c>, which the
    /// family list above had been quietly contradicting since the first commit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This set is what the reference services actually emitted at the sink,
    /// dumped from <c>e2e-instrumented.sh</c> on 2026-09-08 rather than read
    /// off the specification — the empirical half of ADR-0018. Eight of the
    /// twelve come from the NATS client's own instrumentation and were never
    /// named anywhere in this repository; they reached the store on the family
    /// prefix alone.
    /// </para>
    /// <para>
    /// A key absent here is dropped, which is the point: the convention has
    /// moved repeatedly, and a rename upstream should surface as a dropped-key
    /// count and a review rather than as telemetry that silently changes shape.
    /// </para>
    /// </remarks>
    internal static readonly string[] AllowedMessagingKeys =
    [
        // What and where. The subject is structural in this estate — subjects
        // are static names, not templates carrying identifiers.
        "messaging.system",
        "messaging.operation",
        "messaging.destination.name",
        "messaging.destination.template",
        "messaging.destination.temporary",
        "messaging.destination_publish.name",
        "messaging.nats.message.subject",

        // 🔒 Class 2. An opaque per-message identifier, and the reason
        // NeverAMetricDimension carries it: see the note there.
        "messaging.message.id",

        // Sizes and client identity. Class 0.
        "messaging.message.body.size",
        "messaging.message.envelope.size",
        "messaging.client_id",

        // Not a semantic convention key. The reference consumer sets it on a
        // retry (ADR-0025 requires a key we invent to be declared, not admitted
        // by prefix), and dashboard 3.4 reads it. Named here so that stays a
        // decision rather than a side effect of the prefix that used to cover
        // it.
        "messaging.attempt",

        // Specified by docs/diagnostic-queries.md as the dimension for every
        // async pipeline panel, and emitted by nothing yet — the sample uses
        // core NATS with no consumer group. Admitted ahead of use so the
        // allowlist is not the blocker when it does appear; the precedent is
        // db.query.text, carved out costlessly against a component that does
        // not exist yet.
        "messaging.consumer.group.name",
    ];

    /// <summary>
    /// Families allowed on a <b>resource</b>, which is a deliberately narrower
    /// set than <see cref="AllowedFamilies"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A resource describes who is emitting, not what happened, so nothing in
    /// <c>http.</c>, <c>db.</c>, <c>messaging.</c>, <c>url.</c> or
    /// <c>exception.</c> belongs on one. Allowing the span families here would
    /// let a service move a request-scoped value onto the resource and out of
    /// reach of the span rules — the same key, unfiltered, on every signal it
    /// emits.
    /// </para>
    /// <para>
    /// 🔒 This exists because an agent-instrumented service builds its own
    /// resource from <c>OTEL_RESOURCE_ATTRIBUTES</c> (ADR-0009). Our own
    /// services get their resource from <c>ServiceIdentity</c> and are already
    /// constrained; those services are not the threat this table addresses.
    /// </para>
    /// </remarks>
    internal static readonly string[] AllowedResourceFamilies =
    [
        // Identity and provenance. Rev 3 Appendix B, ADR-0006, ADR-0008.
        "service.", "deployment.", "vcs.", "cicd.",

        // Where it ran. Emitted by every SDK and by the 4.8 agent.
        "telemetry.sdk.", "telemetry.distro.", "process.", "host.", "os.",
        "container.", "k8s.",
    ];

    /// <summary>
    /// 🔒 Carve-outs denied by prefix within allowed families. This table and
    /// the next are the compliance argument — a family allow is broad by
    /// intent, and these are what make it safe. Reviewed as security-critical,
    /// not as configuration.
    /// </summary>
    internal static readonly string[] DeniedPrefixes =
    [
        // Class 4. Includes Authorization / Set-Cookie. Opt-in upstream, so
        // this denies a future enablement rather than current behaviour.
        "http.request.header.",
        "http.response.header.",

        // Class 3. Parameter values are row data.
        "db.query.parameter.",
    ];

    /// <summary>🔒 Carve-outs denied as exact keys.</summary>
    internal static readonly string[] DeniedKeys =
    [
        // What SetDbStatementForText = false exists to suppress, plus its
        // pre-rename form. Nothing emits these today — the database is CouchDB
        // (ADR-0023) — carved out costlessly so a future SQL component cannot
        // arrive unnoticed.
        "db.query.text",
        "db.statement",
    ];

    /// <summary>
    /// Class 3 by default, and the one conditional pair in the whole allowlist.
    /// Denied on ordinary spans; allowed on CouchDB spans, where
    /// <c>CouchDbUrlPolicy</c> has already replaced the document
    /// identifier and view key with placeholders.
    /// </summary>
    internal static readonly string[] CouchDbOnlyKeys = ["url.full", "url.query"];

    /// <summary>
    /// 🔒 Carve-outs within the allowed <b>resource</b> families. Every one of
    /// these is emitted by default by at least one SDK or by the 4.8 agent, so
    /// each is a live leak rather than a hypothetical one.
    /// </summary>
    internal static readonly string[] DeniedResourceKeys =
    [
        // Connection strings and credentials are passed as arguments often
        // enough that a command line is Class 3 by default. Both the singular
        // and the array-valued form.
        "process.command_line",
        "process.command_args",

        // The machine account a service runs as. Rev 3 treats an operator
        // identity as personal data, and it is not a diagnostic input.
        "process.owner",
    ];

    /// <summary>
    /// Class 2 keys that must never appear as a metric dimension (Rev 3 D2.1
    /// rule 1), regardless of being allowlisted for spans and logs.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>messaging.message.id</c> is here for a reason worth stating: the
    /// declared Class 2 key is <c>message.id</c> (ADR-0007), and <b>nothing
    /// emits it</b>. The identifier that actually travels is the semantic
    /// convention's <c>messaging.message.id</c>, confirmed at the sink on
    /// 2026-09-08. Listing only the declared spelling left the rule naming a key
    /// that does not exist while the key that does exist was unprotected.
    /// </remarks>
    internal static readonly string[] NeverAMetricDimension =
    [
        "application.id", "correlation.id", "session.id", "causation.id",
        "message.id", "messaging.message.id", "tenant.id",
    ];

    /// <summary>
    /// Verdict for a resource attribute key. Resources carry no declared
    /// Class 2 keys, so unlike <see cref="IsAllowedByFamily"/> there is nothing
    /// to consult before this.
    /// </summary>
    internal static bool IsAllowedResourceKey(string key)
    {
        foreach (var denied in DeniedResourceKeys)
        {
            if (string.Equals(key, denied, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var family in AllowedResourceFamilies)
        {
            if (key.StartsWith(family, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verdict for a key that no policy pack declared. Declared Class 2 keys
    /// are matched exactly by the caller, before this is consulted.
    /// </summary>
    internal static bool IsAllowedByFamily(string key, bool isCouchDbSpan)
    {
        foreach (var denied in DeniedKeys)
        {
            if (string.Equals(key, denied, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var prefix in DeniedPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var conditional in CouchDbOnlyKeys)
        {
            if (string.Equals(key, conditional, StringComparison.Ordinal))
            {
                return isCouchDbSpan;
            }
        }

        // Enumerated before the families, because no family covers these: the
        // messaging.* prefix is deliberately absent from AllowedFamilies.
        foreach (var enumerated in AllowedMessagingKeys)
        {
            if (string.Equals(key, enumerated, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var family in AllowedFamilies)
        {
            if (key.StartsWith(family, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
