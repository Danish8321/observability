namespace Raksawi.Observability;

/// <summary>
/// Declares one attribute key as allowlisted, with its data class. Applied at
/// assembly level by the mechanism package and by each policy pack.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0017: there is no manifest, so there is nothing to drift. This
/// declaration is read at compile time by the analyzer and at run time by
/// <see cref="AttributeAllowlist"/> — one source, two readers.
/// </para>
/// <para>
/// Class 2 keys are declared individually and never by prefix (ADR-0018).
/// Classes 3 and 4 must never be declared here at all: they appear nowhere in
/// telemetry, so an allowlist entry for one is a contradiction and is rejected
/// rather than honoured.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AllowedAttributeKeyAttribute : Attribute
{
    /// <summary>Declares <paramref name="key"/> as allowlisted at <paramref name="dataClass"/>.</summary>
    public AllowedAttributeKeyAttribute(string key, DataClass dataClass)
    {
        Key = key;
        DataClass = dataClass;
    }

    /// <summary>The attribute key, exactly as it appears on a span.</summary>
    public string Key { get; }

    /// <summary>The Rev 3 D2.1 class this key carries.</summary>
    public DataClass DataClass { get; }
}
