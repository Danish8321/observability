using System.Globalization;

namespace Raksawi.Observability;

/// <summary>
/// The standard <c>OTEL_*</c> variables this package reads, and only those
/// (ADR-0034, the table in <c>docs/configuration.md</c>). Applied between the
/// defaults and the code lambda, so code wins.
/// </summary>
/// <remarks>
/// The SDK is then configured explicitly from the options, so no SDK component
/// re-reads a variable and contradicts this order. An empty value is unset; an
/// unparsable one is a startup error naming the variable, never a silent default.
/// </remarks>
internal static class OtelEnvironment
{
    /// <summary>
    /// The kill switch. The one exception to the precedence order: nothing in
    /// code can re-enable a disabled SDK.
    /// </summary>
    internal static bool IsSdkDisabled(Func<string, string?> read)
    {
        var value = Read(read, "OTEL_SDK_DISABLED");
        if (value is null)
        {
            return false;
        }

        if (bool.TryParse(value, out var disabled))
        {
            return disabled;
        }

        throw Invalid("OTEL_SDK_DISABLED", value, "true or false");
    }

    internal static void Apply(RaksawiObservabilityOptions options, Func<string, string?> read)
    {
        if (Read(read, "OTEL_SERVICE_NAME") is { } serviceName)
        {
            options.ServiceName = serviceName;
        }

        if (Read(read, "OTEL_EXPORTER_OTLP_ENDPOINT") is { } endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw Invalid("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint, "an absolute http or https URI");
            }

            options.OtlpEndpoint = uri;
        }

        // Validated, not stored: the exporter is always http/protobuf, and 4317
        // is closed estate-wide.
        if (Read(read, "OTEL_EXPORTER_OTLP_PROTOCOL") is { } protocol && protocol != "http/protobuf")
        {
            throw Invalid("OTEL_EXPORTER_OTLP_PROTOCOL", protocol, "http/protobuf (gRPC is closed estate-wide)");
        }

        if (Read(read, "OTEL_TRACES_SAMPLER") is { } sampler)
        {
            (double ratio, bool ignoresParent) = sampler switch
            {
                "always_on" => (1.0, true),
                "always_off" => (0.0, true),
                "traceidratio" => (Ratio(read), true),
                "parentbased_always_on" => (1.0, false),
                "parentbased_always_off" => (0.0, false),
                "parentbased_traceidratio" => (Ratio(read), false),
                _ => throw Invalid(
                    "OTEL_TRACES_SAMPLER",
                    sampler,
                    "always_on, always_off, traceidratio, parentbased_always_on, parentbased_always_off or parentbased_traceidratio"),
            };

            options.SamplingRatio = ratio;
            options.SamplerIgnoresParent = ignoresParent;
        }
    }

    /// <summary>The ratio argument; the specification's default is 1.0.</summary>
    private static double Ratio(Func<string, string?> read)
    {
        var value = Read(read, "OTEL_TRACES_SAMPLER_ARG");
        if (value is null)
        {
            return 1.0;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) && ratio is >= 0 and <= 1)
        {
            return ratio;
        }

        throw Invalid("OTEL_TRACES_SAMPLER_ARG", value, "a number between 0 and 1");
    }

    private static string? Read(Func<string, string?> read, string key) =>
        read(key) is { Length: > 0 } value ? value.Trim() : null;

    private static InvalidOperationException Invalid(string key, string value, string expected) =>
        new($"{key} is '{value}'; expected {expected} (ADR-0034).");
}
