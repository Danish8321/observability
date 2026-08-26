using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The collector states the allowlist a second time, in OTTL, because it must
/// govern services that contain none of our code (ADR-0009). Two statements of
/// one rule can drift; these tests are what stops the drift being silent.
/// </summary>
/// <remarks>
/// This is not a substitute for Gate 3, which verifies redaction by inspecting
/// stored data. It checks that the two sides say the same thing, not that
/// either is right.
/// </remarks>
public sealed class CollectorAllowlistContractTests
{
    private static readonly string Config = ReadConfig();

    [Theory]
    [MemberData(nameof(AllowedFamilies))]
    public void Every_allowed_family_appears_in_the_collector_keep(string family)
    {
        Assert.Contains(Escaped(family), KeepExpression());
    }

    [Theory]
    [MemberData(nameof(CarveOuts))]
    public void Every_carve_out_appears_in_the_collector_deny(string carveOut)
    {
        // Carve-outs are deleted before the keep, so they must appear in a
        // delete_matching_keys statement, not merely be absent from the keep.
        var deletes = string.Join('\n', Lines("delete_matching_keys"));

        Assert.Contains(Escaped(carveOut), deletes);
    }

    [Fact]
    public void Conditional_pair_is_deleted_on_a_span_with_no_known_host()
    {
        var conditional = Lines("delete_matching_keys").Where(line => line.Contains("url\\\\.(full|query)")
            || line.Contains("url\\.(full|query)")).ToArray();

        // One statement for the absent-host case and one for the wrong-host
        // case. Both must exist: a single statement testing IsMatch against a
        // nil attribute errors, and under error_mode: propagate that drops the
        // batch rather than filtering it.
        Assert.Equal(2, conditional.Length);
        Assert.Contains(conditional, line => line.Contains("== nil"));
        Assert.Contains(conditional, line => line.Contains("IsMatch"));
    }

    [Theory]
    [MemberData(nameof(NeverAMetricDimension))]
    public void Every_unbounded_dimension_is_deleted_from_metric_datapoints(string key)
    {
        var metricSection = Config[Config.IndexOf("metric_statements", StringComparison.Ordinal)..];

        Assert.Contains(Escaped(key), metricSection);
    }

    [Fact]
    public void Keep_is_the_last_span_statement_so_anything_unnamed_is_gone_by_default()
    {
        var spanSection = Config[
            Config.IndexOf("trace_statements", StringComparison.Ordinal)..Config.IndexOf("metric_statements", StringComparison.Ordinal)];

        var statements = spanSection.Split('\n')
            .Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("keep_matching_keys", statements[^1]);
    }

    [Theory]
    [MemberData(nameof(AllowedResourceFamilies))]
    public void Every_allowed_resource_family_appears_in_the_collector_resource_keep(string family)
    {
        Assert.Contains(Escaped(family), ResourceKeep());
    }

    [Theory]
    [MemberData(nameof(DeniedResourceKeys))]
    public void Every_resource_carve_out_appears_in_the_collector_resource_deny(string key)
    {
        var deletes = string.Join('\n', ResourceLines("delete_matching_keys"));

        // Spelled as one alternation rather than three literals, so compare on
        // the leaf rather than the whole key.
        Assert.Contains(key[(key.LastIndexOf('.') + 1)..], deletes);
        Assert.Contains(Escaped("process."), deletes);
    }

    [Theory]
    [InlineData("http.")]
    [InlineData("db.")]
    [InlineData("messaging.")]
    [InlineData("url.")]
    [InlineData("exception.")]
    public void A_span_family_is_not_allowed_on_a_resource(string spanOnlyFamily)
    {
        // 🔒 The resource set is narrower on purpose (ADR-0026). If a span
        // family appears here, a service can move a request-scoped value onto
        // the resource and escape the span rules entirely.
        Assert.DoesNotContain(Escaped(spanOnlyFamily), ResourceKeep());
        Assert.False(AllowlistRules.IsAllowedResourceKey(spanOnlyFamily + "anything"));
    }

    [Fact]
    public void Both_pipelines_state_the_same_resource_rule()
    {
        // The transform processor scopes statements per signal, so the resource
        // rule is written twice. Twice is where drift lives.
        var keeps = ResourceLines("keep_matching_keys").Select(line => line.Trim()).ToArray();
        var deletes = ResourceLines("delete_matching_keys").Select(line => line.Trim()).ToArray();

        Assert.Equal(2, keeps.Length);
        Assert.Equal(2, deletes.Length);
        Assert.Equal(keeps[0], keeps[1]);
        Assert.Equal(deletes[0], deletes[1]);
    }

    [Fact]
    public void Collector_fails_closed_on_an_erroring_statement()
    {
        // silent or ignore would pass unfiltered attributes through to storage.
        Assert.Contains("error_mode: propagate", Config);
    }

    public static TheoryData<string> AllowedFamilies() => Load(AllowlistRules.AllowedFamilies);

    public static TheoryData<string> CarveOuts() =>
        Load([.. AllowlistRules.DeniedPrefixes, .. AllowlistRules.DeniedKeys]);

    public static TheoryData<string> NeverAMetricDimension() => Load(AllowlistRules.NeverAMetricDimension);

    public static TheoryData<string> AllowedResourceFamilies() => Load(AllowlistRules.AllowedResourceFamilies);

    public static TheoryData<string> DeniedResourceKeys() => Load(AllowlistRules.DeniedResourceKeys);

    private static TheoryData<string> Load(string[] values)
    {
        var data = new TheoryData<string>();

        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }

    /// <summary>The key as it is spelled inside an OTTL regex: dots escaped.</summary>
    private static string Escaped(string key) => key.Replace(".", "\\\\.", StringComparison.Ordinal);

    private static string KeepExpression() =>
        string.Join('\n', Lines("keep_matching_keys").Where(line => !line.Contains("resource.attributes", StringComparison.Ordinal)));

    private static string ResourceKeep() => string.Join('\n', ResourceLines("keep_matching_keys"));

    private static IEnumerable<string> ResourceLines(string containing) =>
        Lines(containing).Where(line => line.Contains("resource.attributes", StringComparison.Ordinal));

    private static IEnumerable<string> Lines(string containing) =>
        Config.Split('\n').Where(line => line.Contains(containing, StringComparison.Ordinal));

    /// <summary>
    /// Walks up from the test binary to the repository root. The config is a
    /// deployment asset rather than test content, so it is read where it
    /// actually ships — a copy in the test project could itself drift.
    /// </summary>
    private static string ReadConfig()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "deploy", "collector", "config.yaml");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("deploy/collector/config.yaml not found above " + AppContext.BaseDirectory);
    }
}
