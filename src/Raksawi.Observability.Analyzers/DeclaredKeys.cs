using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Raksawi.Observability.Analyzers;

/// <summary>
/// The Class 2 keys a compilation may say, read from the
/// <c>AllowedAttributeKey</c> assembly attributes on the compilation itself and
/// on every assembly it references (ADR-0017).
/// </summary>
/// <remarks>
/// The runtime reads the same declarations reflectively at startup
/// (<c>AttributeAllowlist.FromLoadedAssemblies</c>). This is the build-time
/// reader of the same source of truth — there is still no manifest, so there is
/// still nothing to drift.
/// </remarks>
internal static class DeclaredKeys
{
    internal const string AttributeMetadataName = "Raksawi.Observability.AllowedAttributeKeyAttribute";

    /// <summary>Class 3 and 4 of the data-class enum, which appear nowhere in telemetry.</summary>
    private const int RestrictedPersonalData = 3;
    private const int Secret = 4;

    internal static HashSet<string> Collect(Compilation compilation)
    {
        var keys = new HashSet<string>();

        Read(compilation.Assembly, keys);

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referenced)
            {
                Read(referenced, keys);
            }
        }

        return keys;
    }

    private static void Read(IAssemblySymbol assembly, HashSet<string> keys)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeMetadataName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is not string key)
            {
                continue;
            }

            // A declaration for Class 3 or 4 is a contradiction. Ignored rather
            // than honoured, exactly as the runtime ignores it — declaring a key
            // must not become the route to emitting it.
            if (attribute.ConstructorArguments[1].Value is int dataClass
                && (dataClass == RestrictedPersonalData || dataClass == Secret))
            {
                continue;
            }

            _ = keys.Add(key);
        }
    }

    /// <summary>
    /// True when the analyzer has no way to know what is declared — no
    /// reference to the mechanism package at all.
    /// </summary>
    /// <remarks>
    /// In that case RKS001 stays silent. A project that does not consume this
    /// platform is not governed by it, and reporting every tag key in such a
    /// project as undeclared would be noise, not enforcement.
    /// </remarks>
    internal static bool PlatformIsAbsent(Compilation compilation) =>
        compilation.GetTypeByMetadataName(AttributeMetadataName) is null;

    /// <summary>
    /// Build-time verdict for a tag key.
    /// </summary>
    /// <remarks>
    /// <c>isCouchDbSpan: true</c> is deliberate. The conditional pair
    /// (<c>url.full</c>, <c>url.query</c>) depends on the span's target host,
    /// which is a run-time fact. The analyzer takes the permissive branch and
    /// leaves that verdict to the runtime processor, because a warning is not a
    /// control — the drop is. Guessing the other way would warn on every
    /// legitimate CouchDB call.
    /// </remarks>
    internal static bool IsAllowed(string key, HashSet<string> declared) =>
        declared.Contains(key) || AllowlistRules.IsAllowedByFamily(key, isCouchDbSpan: true);

    /// <summary>Keys the analyzer treats as metric dimensions to be refused.</summary>
    internal static bool IsNeverAMetricDimension(string key) =>
        AllowlistRules.NeverAMetricDimension.Contains(key);
}
