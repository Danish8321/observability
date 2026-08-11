using System.Diagnostics;
using OpenTelemetry.Resources;

namespace Raksawi.Observability;

/// <summary>
/// Resource identity and the trace-context format check. Both runtimes compile
/// this file, which is the point of the multi-target: governance lives in the
/// shared compilation rather than in per-runtime wiring instructions (ADR-0001).
/// </summary>
internal static class ServiceIdentity
{
    /// <summary>
    /// Forces the W3C trace context format. Default on .NET 5+, but
    /// <see cref="ActivityIdFormat.Hierarchical"/> on .NET Framework, where the
    /// failure is silent: traces split in two rather than erroring.
    /// </summary>
    /// <returns>
    /// Null when the format was already correct, otherwise a warning describing
    /// what was corrected. Per ADR-0005 this warns and never throws — telemetry
    /// setup must not be able to fail a service start (Rev 3 I3.6).
    /// </returns>
    public static string? EnsureW3CTraceContext()
    {
        var wasHierarchical = Activity.DefaultIdFormat != ActivityIdFormat.W3C;

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        return wasHierarchical
            ? "Activity.DefaultIdFormat was not W3C and has been corrected. On " +
              ".NET Framework this is expected at startup; if it appears after " +
              "an Activity has already been created, traces have already split."
            : null;
    }

    public static ResourceBuilder BuildResource(RaksawiObservabilityOptions options)
    {
        var attributes = new List<KeyValuePair<string, object>>
        {
            new("service.namespace", options.ServiceNamespace),
            new("service.instance.id", ResolveInstanceId(options)),
        };

        return ResourceBuilder.CreateDefault()
            .AddService(serviceName: options.ServiceName)
            .AddAttributes(attributes);
    }

    /// <summary>
    /// ADR-0008: supplied if known, derived if not. The derived form must be
    /// stable across an app pool recycle, so nothing process-scoped is used.
    /// </summary>
    private static string ResolveInstanceId(RaksawiObservabilityOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceInstanceId))
        {
            return options.ServiceInstanceId!;
        }

        var supplied = Environment.GetEnvironmentVariable("OTEL_SERVICE_INSTANCE_ID");
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            return supplied!;
        }

        return $"{Environment.MachineName}:{options.ServiceName}".ToLowerInvariant();
    }
}
