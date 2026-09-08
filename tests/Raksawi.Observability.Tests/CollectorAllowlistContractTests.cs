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
    [MemberData(nameof(AllowedMessagingKeys))]
    public void Every_enumerated_messaging_key_appears_in_the_collector_keep(string key)
    {
        Assert.Contains(Escaped(key), KeepExpression());
    }

    [Theory]
    [MemberData(nameof(AllowedMessagingKeys))]
    public void Every_enumerated_messaging_key_appears_in_the_collector_log_keep(string key)
    {
        Assert.Contains(Escaped(key), LogKeep());
    }

    [Fact]
    public void Messaging_is_not_admitted_as_a_family_on_either_side()
    {
        // 🔒 The stability policy in docs/allowlist.md says messaging.* is
        // enumerated individually because the convention keeps moving. Both
        // sides had been admitting the whole prefix anyway, which is what this
        // asserts can no longer happen: a family allow here would let a renamed
        // or newly invented messaging key through all three enforcement points
        // without review.
        Assert.DoesNotContain("messaging.", AllowlistRules.AllowedFamilies);
        Assert.False(AllowlistRules.IsAllowedByFamily("messaging.invented.key", isCouchDbSpan: false));

        // The collector states it in OTTL. "messaging\\.|" would be the family
        // form; every legitimate spelling here is followed by a key segment.
        Assert.DoesNotContain("messaging\\\\.|", KeepExpression());
        Assert.DoesNotContain("messaging\\\\.|", LogKeep());
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
        var conditional = Section("trace_statements", "metric_statements").Split('\n')
            .Where(line => line.Contains("delete_matching_keys", StringComparison.Ordinal)
                && line.Contains("url", StringComparison.Ordinal))
            .ToArray();

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
        Assert.Contains(Escaped(key), Section("metric_statements", "log_statements"));
    }

    [Fact]
    public void Keep_is_the_last_span_statement_so_anything_unnamed_is_gone_by_default()
    {
        Assert.Contains("keep_matching_keys", LastStatementIn("trace_statements", "metric_statements"));
    }

    [Theory]
    [MemberData(nameof(AllowedFamilies))]
    public void Every_allowed_family_appears_in_the_collector_log_keep(string family)
    {
        // 🔒 Class 2 is permitted on spans and logs (Rev 3 D2.1), so the log
        // keep is the span keep rather than a narrower copy of it. A family
        // present on one and missing from the other is drift either way.
        Assert.Contains(Escaped(family), LogKeep());
    }

    [Theory]
    [MemberData(nameof(CarveOuts))]
    public void Every_carve_out_appears_in_the_collector_log_deny(string carveOut)
    {
        var deletes = string.Join('\n', LogLines("delete_matching_keys"));

        Assert.Contains(Escaped(carveOut), deletes);
    }

    [Fact]
    public void The_conditional_pair_is_unconditional_on_a_log_record()
    {
        // url.full survives on a span to a known CouchDB host because
        // CouchDbUrlPolicy replaced the document identifier first. Nothing
        // redacts a URL on a log record, so the exemption has no precondition
        // and the pair is deleted outright — one statement, no host test.
        var conditional = LogLines("delete_matching_keys")
            .Where(line => line.Contains("url", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(conditional);
        Assert.DoesNotContain("where", conditional[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_is_the_last_log_statement_so_anything_unnamed_is_gone_by_default()
    {
        Assert.Contains("keep_matching_keys", LastStatementIn("log_statements", "  resource:"));
    }

    [Fact]
    public void Every_signal_is_filtered_before_it_reaches_the_exporter()
    {
        // A signal with no pipeline is not exported at all, which is loud. A
        // pipeline that skips transform/allowlist is silent, and is the one
        // this catches.
        foreach (var signal in new[] { "traces", "metrics", "logs" })
        {
            var pipeline = Section("    " + signal + ":", "exporters: [otlp/signoz]");

            Assert.Contains("transform/allowlist", pipeline);
        }
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

        // Three copies now: traces, metrics and logs.
        Assert.Equal(3, keeps.Length);
        Assert.Equal(3, deletes.Length);
        Assert.Single(keeps.Distinct());
        Assert.Single(deletes.Distinct());
    }

    [Fact]
    public void Collector_fails_closed_on_an_erroring_statement()
    {
        // silent or ignore would pass unfiltered attributes through to storage.
        Assert.Contains("error_mode: propagate", Config);
    }

    public static TheoryData<string> AllowedFamilies() => Load(AllowlistRules.AllowedFamilies);

    public static TheoryData<string> AllowedMessagingKeys() => Load(AllowlistRules.AllowedMessagingKeys);

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

    private static string LogKeep() => string.Join('\n', LogLines("keep_matching_keys"));

    /// <summary>Lines of the log pipeline only, resource statements excluded.</summary>
    private static IEnumerable<string> LogLines(string containing) =>
        Section("log_statements", "  resource:").Split('\n')
            .Where(line => line.Contains(containing, StringComparison.Ordinal)
                && !line.Contains("resource.attributes", StringComparison.Ordinal));

    /// <summary>The configuration between two markers, exclusive of the second.</summary>
    private static string Section(string from, string to)
    {
        var start = Config.IndexOf(from, StringComparison.Ordinal);
        var end = Config.IndexOf(to, start, StringComparison.Ordinal);

        return end < 0 ? Config[start..] : Config[start..end];
    }

    private static string LastStatementIn(string from, string to) =>
        Section(from, to).Split('\n')
            .Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .ToArray()[^1];

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
