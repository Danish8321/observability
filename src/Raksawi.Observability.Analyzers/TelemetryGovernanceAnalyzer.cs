using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Raksawi.Observability.Analyzers;

/// <summary>
/// The build-time enforcement point of ADR-0003 (ADR-0002 for the analyzer
/// decision itself). Everything it can see, it checks; what it cannot see is
/// still caught by the runtime processor and the collector.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately literal-only. A key built at run time
/// (<c>$"app.{name}"</c>, a variable, a constant from another assembly) is not
/// flagged, because guessing produces false positives, and a false positive on
/// a governance rule teaches people to suppress governance rules. The runtime
/// allowlist has no such limitation: it sees the actual key.
/// </para>
/// <para>
/// Operates on <c>IOperation</c> rather than syntax so the same rules hold
/// regardless of how the call was spelled.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TelemetryGovernanceAnalyzer : DiagnosticAnalyzer
{
    private const string ActivityTypeName = "System.Diagnostics.Activity";
    private const string MetricsNamespace = "System.Diagnostics.Metrics";

    /// <summary>Method names that assemble an exporter pipeline by hand (RKS003).</summary>
    private static readonly ImmutableHashSet<string> ExporterMethods = ImmutableHashSet.Create(
        "AddOtlpExporter", "AddConsoleExporter", "AddInMemoryExporter");

    /// <summary>Methods that set a tag on an <see cref="ActivityTypeName"/>.</summary>
    private static readonly ImmutableHashSet<string> TagMethods = ImmutableHashSet.Create(
        "SetTag", "AddTag", "AddBaggage", "SetBaggage");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.UndeclaredAttributeKey,
            Diagnostics.ClassTwoAsMetricDimension,
            Diagnostics.ExporterConfiguredDirectly);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            // The declared set is a property of the compilation, so it is read
            // once rather than per call site.
            var declared = DeclaredKeys.Collect(start.Compilation);
            var platformAbsent = DeclaredKeys.PlatformIsAbsent(start.Compilation);

            start.RegisterOperationAction(
                operation => Analyze((IInvocationOperation)operation.Operation, operation, declared, platformAbsent),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        HashSet<string> declared,
        bool platformAbsent)
    {
        var method = invocation.TargetMethod;

        if (ExporterMethods.Contains(method.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ExporterConfiguredDirectly,
                invocation.Syntax.GetLocation(),
                method.Name));
            return;
        }

        if (IsMetricInstrument(method.ContainingType))
        {
            ReportMetricDimensions(invocation.Arguments, context);
            return;
        }

        if (!platformAbsent
            && TagMethods.Contains(method.Name)
            && IsActivity(method.ContainingType)
            && TryGetLiteralKey(invocation.Arguments.FirstOrDefault()?.Value, out var key)
            && !DeclaredKeys.IsAllowed(key, declared))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UndeclaredAttributeKey,
                invocation.Arguments[0].Value.Syntax.GetLocation(),
                key));
        }
    }

    /// <summary>
    /// 🔒 RKS002. Every string literal in the argument list is checked, not
    /// only the one in key position: a tag key can arrive as the first element
    /// of a collection initializer, as a <c>KeyValuePair</c> constructor
    /// argument, or as one of a params array, and the rule is absolute enough
    /// that missing a spelling is worse than checking a value that happens to
    /// equal an identifier name.
    /// </summary>
    private static void ReportMetricDimensions(
        ImmutableArray<IArgumentOperation> arguments,
        OperationAnalysisContext context)
    {
        foreach (var argument in arguments)
        {
            foreach (var literal in Literals(argument.Value))
            {
                if (TryGetLiteralKey(literal, out var key) && DeclaredKeys.IsNeverAMetricDimension(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ClassTwoAsMetricDimension,
                        literal.Syntax.GetLocation(),
                        key));
                }
            }
        }
    }

    /// <summary>
    /// Unwraps conversions, params arrays, and inline tag objects to reach the
    /// literals underneath — <c>new KeyValuePair&lt;string, object?&gt;(...)</c>
    /// and <c>new TagList { { "k", v } }</c> both appear as arguments to the
    /// instrument call and both have to be looked inside.
    /// </summary>
    /// <remarks>
    /// A tag collection built into a local and passed by name is not followed.
    /// Same reason RKS001 is literal-only: what the analyzer cannot see for
    /// certain, it leaves to the runtime, which sees the actual key.
    /// </remarks>
    private static IEnumerable<IOperation> Literals(IOperation operation)
    {
        switch (operation)
        {
            case IConversionOperation conversion:
                return Literals(conversion.Operand);

            case IArrayCreationOperation array when array.Initializer is not null:
                return array.Initializer.ElementValues.SelectMany(Literals);

            case IObjectCreationOperation creation:
                var arguments = creation.Arguments.SelectMany(argument => Literals(argument.Value));

                return creation.Initializer is null
                    ? arguments
                    : arguments.Concat(creation.Initializer.Initializers
                        .OfType<IInvocationOperation>()
                        .SelectMany(add => add.Arguments)
                        .SelectMany(argument => Literals(argument.Value)));

            default:
                return new[] { operation };
        }
    }

    private static bool TryGetLiteralKey(IOperation? operation, out string key)
    {
        key = string.Empty;

        if (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        if (operation?.ConstantValue is { HasValue: true, Value: string literal })
        {
            key = literal;
            return true;
        }

        return false;
    }

    private static bool IsActivity(INamedTypeSymbol? type) =>
        type?.ToDisplayString() == ActivityTypeName;

    private static bool IsMetricInstrument(INamedTypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() == MetricsNamespace;
}
