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
    /// <see cref="AllowlistRules.CouchDbOnlyKeys"/> depend on it.
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

        // Families and carve-outs live in AllowlistRules, which the analyzer
        // package compiles too. Duplicating them here would let build-time and
        // run-time enforcement drift apart silently.
        return AllowlistRules.IsAllowedByFamily(key, isCouchDbSpan);
    }
}
