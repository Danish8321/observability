using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Raksawi.Observability;
using Raksawi.Observability.Analyzers;

namespace Raksawi.Observability.Tests;

/// <summary>
/// Compiles snippets in memory and reads the analyzer's diagnostics back.
/// </summary>
/// <remarks>
/// The snippets reference the real <c>Raksawi.Observability</c> assembly, so
/// the declared Class 2 keys the analyzer finds are the ones actually shipped
/// in <c>AssemblyInfo.cs</c> — not a fixture that could quietly disagree with
/// them.
/// </remarks>
public sealed class TelemetryGovernanceAnalyzerTests
{
    [Fact]
    public void Key_in_an_allowed_family_raises_nothing()
    {
        Assert.Empty(Diagnose("""activity.SetTag("http.request.method", "GET");"""));
    }

    [Fact]
    public void Key_declared_by_the_mechanism_package_raises_nothing()
    {
        Assert.Empty(Diagnose("""activity.SetTag("correlation.id", "c-1");"""));
    }

    [Fact]
    public void Undeclared_key_raises_RKS001()
    {
        Assert.Equal(["RKS001"], Diagnose("""activity.SetTag("applicantIdentifier", "x");"""));
    }

    [Fact]
    public void Carved_out_key_raises_RKS001_even_though_its_family_is_allowed()
    {
        // db. is an allowed family; db.statement is a carve-out inside it.
        Assert.Equal(["RKS001"], Diagnose("""activity.SetTag("db.statement", "SELECT 1");"""));
    }

    [Fact]
    public void Conditional_key_raises_nothing_because_the_host_is_a_run_time_fact()
    {
        // url.full is allowed only on CouchDB spans. The analyzer cannot know
        // the target host, so it defers to the runtime processor rather than
        // warning on every legitimate CouchDB call.
        Assert.Empty(Diagnose("""activity.SetTag("url.full", "https://couch/db/_all_docs");"""));
    }

    [Fact]
    public void Non_literal_key_raises_nothing()
    {
        // Deliberate: guessing at a computed key produces false positives, and
        // the runtime allowlist sees the actual key anyway.
        Assert.Empty(Diagnose("""activity.SetTag("app." + System.Guid.NewGuid(), "x");"""));
    }

    [Fact]
    public void Class_two_as_a_metric_dimension_raises_RKS002()
    {
        Assert.Equal(
            ["RKS002"],
            Diagnose("""counter.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("correlation.id", "c-1"));"""));
    }

    [Fact]
    public void Class_two_in_a_TagList_initializer_raises_RKS002()
    {
        Assert.Equal(
            ["RKS002"],
            Diagnose("""counter.Add(1, new System.Diagnostics.TagList { { "application.id", "a-1" } });"""));
    }

    [Fact]
    public void Class_two_in_a_tag_list_built_into_a_local_raises_nothing()
    {
        // Known and accepted: the analyzer does not follow a collection built
        // elsewhere. RKS002 is a build-time convenience; the binding control is
        // the runtime allowlist, which sees the actual key.
        Assert.Empty(Diagnose("""
            var tags = new System.Diagnostics.TagList { { "application.id", "a-1" } };
            counter.Add(1, tags);
            """));
    }

    [Fact]
    public void Class_two_on_a_span_raises_nothing_because_only_metrics_are_bounded()
    {
        Assert.Empty(Diagnose("""activity.SetTag("correlation.id", "c-1");"""));
    }

    [Fact]
    public void Bounded_metric_dimension_raises_nothing()
    {
        Assert.Empty(Diagnose("""
            counter.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("outcome", "cleared"));
            """));
    }

    [Fact]
    public void Hand_configured_exporter_raises_RKS003()
    {
        Assert.Equal(["RKS003"], Diagnose("Pipeline.AddOtlpExporter();"));
    }

    /// <summary>
    /// Compiles <paramref name="body"/> inside a method that already has an
    /// <c>Activity</c> and a <c>Counter</c> in scope, and returns the ids of
    /// the diagnostics this analyzer raised, in source order.
    /// </summary>
    private static string[] Diagnose(string body)
    {
        var source = $$"""
            using System.Diagnostics;
            using System.Diagnostics.Metrics;

            internal static class Pipeline
            {
                internal static void AddOtlpExporter() { }
            }

            internal static class Subject
            {
                internal static void Run(Activity activity, Counter<long> counter)
                {
                    {{body}}
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(source)],
            ReferencePaths().Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // A snippet that does not compile would make every assertion below
        // meaningless — no operations means no diagnostics, and the test would
        // pass vacuously.
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TelemetryGovernanceAnalyzer()));

        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => diagnostic.Id)
            .ToArray();
    }

    private static IEnumerable<string> ReferencePaths()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(DataClass).Assembly.Location)
            .Append(typeof(Activity).Assembly.Location)
            .Append(typeof(Counter<>).Assembly.Location)
            .Distinct();
    }
}
