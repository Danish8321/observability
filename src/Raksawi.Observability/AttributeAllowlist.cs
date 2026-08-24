using System.Reflection;

namespace Raksawi.Observability;

/// <summary>
/// The runtime allowlist: an attribute whose key is not allowlisted is dropped
/// before export (ADR-0003). Allow by family, deny by carve-out (ADR-0018).
/// </summary>
/// <remarks>
/// <para>
/// This is the primary source-side control, not a formality. Rev 3 I3.2 makes
/// the collector the net rather than the control, and Gate 3 verifies redaction
/// against stored data — that check is only evidence if something real runs
/// here.
/// </para>
/// <para>
/// A deny-list was rejected: it catches only key names someone predicted, and
/// an attribute named <c>applicantIdentifier</c> carrying a CPR passes one.
/// Value-pattern scanning was rejected here too — regex over every value on
/// every span is unbounded work on the request path, which Rev 3 I3.6 forbids.
/// That cost belongs at the collector.
/// </para>
/// </remarks>
internal sealed class AttributeAllowlist
{
    /// <summary>
    /// Families allowed by prefix, per docs/allowlist.md. Stable families need
    /// no attention on a semconv upgrade; that is the point of allowing by
    /// prefix rather than enumerating.
    /// </summary>
    private static readonly string[] AllowedFamilies =
    [
        "service.", "deployment.", "vcs.", "cicd.",
        "telemetry.sdk.", "process.", "host.",
        "http.", "url.", "server.", "client.", "network.",
        "db.", "messaging.", "code.", "exception.", "user_agent.",
    ];

    /// <summary>
    /// 🔒 Carve-outs denied within allowed families. This table is the
    /// compliance argument — a family allow is broad by intent, and these are
    /// what make it safe. Reviewed as security-critical, not as configuration.
    /// </summary>
    private static readonly string[] DeniedPrefixes =
    [
        // Class 4. Includes Authorization / Set-Cookie. Opt-in upstream, so
        // this denies a future enablement rather than current behaviour.
        "http.request.header.",
        "http.response.header.",

        // Class 3. Parameter values are row data.
        "db.query.parameter.",
    ];

    /// <summary>🔒 Carve-outs denied as exact keys.</summary>
    private static readonly string[] DeniedKeys =
    [
        // What SetDbStatementForText = false exists to suppress, plus its
        // pre-rename form. Nothing emits these today — the database is CouchDB
        // (ADR-0023) — carved out costlessly so a future SQL component cannot
        // arrive unnoticed.
        "db.query.text",
        "db.statement",
    ];

    /// <summary>
    /// Class 3 by default, and the one conditional case in the whole allowlist.
    /// Denied on ordinary spans; allowed on CouchDB spans, where
    /// <see cref="CouchDbUrlPolicy"/> has already replaced the document
    /// identifier and view key with placeholders.
    /// </summary>
    /// <remarks>
    /// Both rows in docs/allowlist.md are literally true because of this split:
    /// <c>url.full</c>/<c>url.query</c> carry identifiers where routes encode
    /// them (Rev 3 D0.4b) and are denied; on CouchDB spans the URL *is* the
    /// database surface (ADR-0023) and the redacted shape is the whole
    /// diagnostic value of the span, so it survives.
    /// </remarks>
    private static readonly string[] CouchDbOnlyKeys = ["url.full", "url.query"];

    private readonly HashSet<string> _declaredKeys;

    private AttributeAllowlist(HashSet<string> declaredKeys) => _declaredKeys = declaredKeys;

    /// <summary>
    /// Builds an allowlist over an explicit set of declared Class 2 keys,
    /// rather than whatever the current AppDomain happens to have loaded.
    /// </summary>
    /// <remarks>
    /// The families and carve-outs are the same either way — only the declared
    /// keys are supplied. Exists so the carve-out table can be tested for what
    /// it denies without a test's outcome depending on which assemblies the
    /// runner loaded.
    /// </remarks>
    internal static AttributeAllowlist ForDeclaredKeys(params string[] declaredKeys) =>
        new(new HashSet<string>(declaredKeys, StringComparer.Ordinal));

    /// <summary>
    /// Reads every <see cref="AllowedAttributeKeyAttribute"/> declared by the
    /// assemblies currently loaded, keeping only those whose declaring assembly
    /// carries the same public key token as this one.
    /// </summary>
    /// <remarks>
    /// 🔒 The provenance check is ADR-0017's closed-set mechanism. It is only
    /// as strong as strong-naming: until both packages are actually signed,
    /// every assembly presents an empty token and the check passes vacuously.
    /// That gap is real and is recorded rather than papered over — see
    /// docs/allowlist.md.
    /// </remarks>
    public static AttributeAllowlist FromLoadedAssemblies()
    {
        var expectedToken = typeof(AttributeAllowlist).Assembly.GetName().GetPublicKeyToken();
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<AllowedAttributeKeyAttribute> attributes;
            try
            {
                attributes = assembly.GetCustomAttributes<AllowedAttributeKeyAttribute>();
            }
            catch (Exception)
            {
                // A dynamic or partially-loaded assembly must not be able to
                // fail telemetry setup, let alone a service start (Rev 3 I3.6).
                continue;
            }

            if (!HasMatchingToken(assembly, expectedToken))
            {
                continue;
            }

            foreach (var attribute in attributes)
            {
                // Classes 3 and 4 appear nowhere in telemetry. A declaration
                // for one is a contradiction, so it is ignored rather than
                // honoured — declaring it must not be a route to emitting it.
                if (attribute.DataClass is DataClass.RestrictedPersonalData or DataClass.Secret)
                {
                    continue;
                }

                _ = declared.Add(attribute.Key);
            }
        }

        return new AttributeAllowlist(declared);
    }

    private static bool HasMatchingToken(Assembly assembly, byte[]? expectedToken)
    {
        var token = assembly.GetName().GetPublicKeyToken();

        if (expectedToken is null || expectedToken.Length == 0)
        {
            // Unsigned build: nothing to compare against. Fail open here
            // deliberately — the alternative is dropping every declared key and
            // silently emptying the allowlist, which is a worse failure than
            // the weak provenance it would be protecting.
            return true;
        }

        if (token is null || token.Length != expectedToken.Length)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            if (token[i] != expectedToken[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="key"/> may be exported.
    /// </summary>
    /// <param name="key">The attribute key, exactly as set on the span.</param>
    /// <param name="isCouchDbSpan">
    /// Whether this span is an outbound call to a configured CouchDB host. Only
    /// <see cref="CouchDbOnlyKeys"/> depend on it.
    /// </param>
    public bool IsAllowed(string key, bool isCouchDbSpan)
    {
        // Declared Class 2 keys are exact matches and never prefixes (ADR-0018).
        // Checked first so a policy pack can name a key inside an otherwise
        // carved-out space if it ever needs to, deliberately and in review.
        if (_declaredKeys.Contains(key))
        {
            return true;
        }

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
