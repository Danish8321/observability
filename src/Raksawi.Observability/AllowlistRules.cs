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
        "db.", "messaging.", "code.", "exception.", "user_agent.",
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
    /// Class 2 keys that must never appear as a metric dimension (Rev 3 D2.1
    /// rule 1), regardless of being allowlisted for spans and logs.
    /// </summary>
    internal static readonly string[] NeverAMetricDimension =
    [
        "application.id", "correlation.id", "session.id", "causation.id",
        "message.id", "tenant.id",
    ];

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
