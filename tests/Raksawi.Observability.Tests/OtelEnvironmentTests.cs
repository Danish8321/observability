using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The standard <c>OTEL_*</c> variables, resolved defaults → environment →
/// code (ADR-0034, plan contract C2). The environment is injected, so these
/// never touch the process environment and cannot race each other.
/// </summary>
// AddRaksawiObservability records the process-wide trace-context gauge that
// W3CTraceContextTests asserts on.
[Collection("Process-wide trace context")]
public class OtelEnvironmentTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        key => values.FirstOrDefault(pair => pair.Key == key).Value;

    private static RaksawiObservabilityOptions Resolve(
        Func<string, string?> environment,
        Action<RaksawiObservabilityOptions>? configure = null)
    {
        var options = new RaksawiObservabilityOptions { ServiceNamespace = "treasury" };
        OtelEnvironment.Apply(options, environment);
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public void Nothing_set_changes_nothing()
    {
        var options = Resolve(Env());

        Assert.Equal(string.Empty, options.ServiceName);
        Assert.Equal(new Uri("http://localhost:4318"), options.OtlpEndpoint);
        Assert.Null(options.SamplingRatio);
    }

    [Fact]
    public void An_empty_value_is_unset()
    {
        var options = Resolve(Env(("OTEL_SERVICE_NAME", ""), ("OTEL_TRACES_SAMPLER", "")));

        Assert.Equal(string.Empty, options.ServiceName);
        Assert.Null(options.SamplingRatio);
    }

    [Fact]
    public void Service_name_and_endpoint_come_from_the_environment()
    {
        var options = Resolve(Env(
            ("OTEL_SERVICE_NAME", "ledger-api"),
            ("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector:4318")));

        Assert.Equal("ledger-api", options.ServiceName);
        Assert.Equal(new Uri("http://collector:4318"), options.OtlpEndpoint);
    }

    [Fact]
    public void Code_wins_over_the_environment()
    {
        var options = Resolve(
            Env(("OTEL_SERVICE_NAME", "from-env"), ("OTEL_EXPORTER_OTLP_ENDPOINT", "http://env:4318")),
            o =>
            {
                o.ServiceName = "from-code";
                o.OtlpEndpoint = new Uri("http://code:4318");
            });

        Assert.Equal("from-code", options.ServiceName);
        Assert.Equal(new Uri("http://code:4318"), options.OtlpEndpoint);
    }

    [Theory]
    [InlineData("collector:4318")]
    [InlineData("/v1")]
    [InlineData("ftp://collector")]
    public void An_endpoint_that_is_not_an_absolute_http_uri_fails_naming_the_variable(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(Env(("OTEL_EXPORTER_OTLP_ENDPOINT", value))));

        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", error.Message);
    }

    [Fact]
    public void Http_protobuf_is_accepted()
    {
        Resolve(Env(("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")));
    }

    [Theory]
    [InlineData("grpc")]
    [InlineData("http/json")]
    public void Any_other_protocol_fails_naming_the_variable(string value)
    {
        // 4317 is closed estate-wide, and the exporter is always configured
        // for http/protobuf, so accepting another value would be a lie.
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(Env(("OTEL_EXPORTER_OTLP_PROTOCOL", value))));

        Assert.Contains("OTEL_EXPORTER_OTLP_PROTOCOL", error.Message);
    }

    [Theory]
    // A parent-based sampler follows the parent's decision either way; the
    // other three decide alone.        root   sampled parent   unsampled parent
    [InlineData("always_on", null, true, true, true)]
    [InlineData("always_off", null, false, false, false)]
    [InlineData("traceidratio", "1", true, true, true)]
    [InlineData("traceidratio", "0", false, false, false)]
    [InlineData("parentbased_always_on", null, true, true, false)]
    [InlineData("parentbased_always_off", null, false, true, false)]
    [InlineData("parentbased_traceidratio", "1", true, true, false)]
    [InlineData("parentbased_traceidratio", "0", false, true, false)]
    public void Each_standard_sampler_decides_as_specified(
        string sampler, string? argument, bool root, bool underSampledParent, bool underUnsampledParent)
    {
        var environment = argument is null
            ? Env(("OTEL_TRACES_SAMPLER", sampler))
            : Env(("OTEL_TRACES_SAMPLER", sampler), ("OTEL_TRACES_SAMPLER_ARG", argument));

        var built = RaksawiPipeline.CreateSampler(Resolve(environment));

        Assert.Equal(root, Samples(built, parent: default));
        Assert.Equal(underSampledParent, Samples(built, Parent(ActivityTraceFlags.Recorded)));
        Assert.Equal(underUnsampledParent, Samples(built, Parent(ActivityTraceFlags.None)));
    }

    [Fact]
    public void A_ratio_sampler_without_an_argument_samples_everything()
    {
        // The specification's default for the argument is 1.0.
        Assert.Equal(1.0, Resolve(Env(("OTEL_TRACES_SAMPLER", "traceidratio"))).SamplingRatio);
    }

    [Fact]
    public void A_ratio_set_in_code_is_parent_based_whatever_the_environment_said()
    {
        // C2: Sampling:Ratio maps to parentbased_traceidratio. A code setting
        // must not inherit "ignore the parent" from an environment it overrode.
        var options = Resolve(Env(("OTEL_TRACES_SAMPLER", "always_on")), o => o.SamplingRatio = 1.0);

        Assert.False(Samples(RaksawiPipeline.CreateSampler(options), Parent(ActivityTraceFlags.None)));
    }

    [Theory]
    [InlineData("OTEL_TRACES_SAMPLER", "probably")]
    [InlineData("OTEL_TRACES_SAMPLER_ARG", "half")]
    [InlineData("OTEL_TRACES_SAMPLER_ARG", "1.5")]
    public void An_unparsable_sampler_fails_naming_the_variable(string key, string value)
    {
        var environment = key == "OTEL_TRACES_SAMPLER"
            ? Env((key, value))
            : Env(("OTEL_TRACES_SAMPLER", "traceidratio"), (key, value));

        var error = Assert.Throws<InvalidOperationException>(() => Resolve(environment));

        Assert.Contains(key, error.Message);
    }

    [Fact]
    public void A_sampler_from_the_environment_satisfies_ADR_0010_outside_development()
    {
        var options = Resolve(Env(("OTEL_SERVICE_NAME", "ledger-api"), ("OTEL_TRACES_SAMPLER", "parentbased_always_on")));

        options.Validate();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void Sdk_disabled_reads_true_or_false(string value, bool disabled)
    {
        Assert.Equal(disabled, OtelEnvironment.IsSdkDisabled(Env(("OTEL_SDK_DISABLED", value))));
    }

    [Fact]
    public void Sdk_disabled_with_any_other_value_fails_naming_the_variable()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => OtelEnvironment.IsSdkDisabled(Env(("OTEL_SDK_DISABLED", "yes"))));

        Assert.Contains("OTEL_SDK_DISABLED", error.Message);
    }

    [Fact]
    public void Sdk_disabled_registers_no_provider_whatever_the_code_says()
    {
        // The one exception to the precedence order: a kill switch nothing
        // can re-enable. No provider means nothing is exported.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.AddRaksawiObservability(
            o =>
            {
                o.ServiceName = "ledger-api";
                o.ServiceNamespace = "treasury";
                o.SamplingRatio = 1.0;
            },
            Env(("OTEL_SDK_DISABLED", "true")));

        using var host = builder.Build();
        Assert.Null(host.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void Sdk_enabled_registers_the_provider()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.AddRaksawiObservability(
            o =>
            {
                o.ServiceName = "ledger-api";
                o.ServiceNamespace = "treasury";
                o.SamplingRatio = 1.0;
            },
            Env());

        using var host = builder.Build();
        Assert.NotNull(host.Services.GetService<TracerProvider>());
    }

    [Theory]
    [InlineData("http://collector:4318", "http://collector:4318/v1/traces")]
    [InlineData("http://collector:4318/", "http://collector:4318/v1/traces")]
    [InlineData("http://gateway/otlp", "http://gateway/otlp/v1/traces")]
    public void The_signal_path_is_appended_to_the_endpoint_not_substituted_for_its_last_segment(
        string endpoint, string used)
    {
        // The standard appends v1/traces to OTEL_EXPORTER_OTLP_ENDPOINT. Uri
        // resolution would replace "otlp" instead, silently exporting nowhere.
        var options = new RaksawiObservabilityOptions { OtlpEndpoint = new Uri(endpoint) };
        var otlp = new OtlpExporterOptions();

        RaksawiPipeline.ConfigureOtlp(otlp, options, "v1/traces");

        Assert.Equal(new Uri(used), otlp.Endpoint);
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, otlp.Protocol);
    }

    private static ActivityContext Parent(ActivityTraceFlags flags) =>
        new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), flags, isRemote: true);

    private static bool Samples(Sampler sampler, ActivityContext parent)
    {
        var parameters = new SamplingParameters(
            parent, ActivityTraceId.CreateRandom(), "work", ActivityKind.Internal);

        return sampler.ShouldSample(parameters).Decision == SamplingDecision.RecordAndSample;
    }
}
