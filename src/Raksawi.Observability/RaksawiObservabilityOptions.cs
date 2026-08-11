namespace Raksawi.Observability;

/// <summary>
/// Configuration for the observability stack. Deliberately small: the values
/// here are the ones that differ per service or per environment, and nothing
/// else is configurable.
/// </summary>
public sealed class RaksawiObservabilityOptions
{
    /// <summary>
    /// Bare service name, kebab-case, per ADR-0006. Never derived from a
    /// deployment-owned name such as a hostname, container name, or app pool.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// The domain this service belongs to, per ADR-0006.
    /// </summary>
    public string ServiceNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Stable identity for this instance, per ADR-0008. Supplied via
    /// OTEL_SERVICE_INSTANCE_ID when known; derived from the host when not.
    /// An app pool recycle does not change it.
    /// </summary>
    public string? ServiceInstanceId { get; set; }

    /// <summary>
    /// OTLP endpoint. http/protobuf on 4318 — gRPC on 4317 is unsupported on
    /// .NET Framework and closed estate-wide.
    /// </summary>
    public Uri OtlpEndpoint { get; set; } = new("http://localhost:4318");

    /// <summary>
    /// Head sampling ratio. Absent configuration is a production boot failure
    /// per ADR-0010; this default applies only when <see cref="IsDevelopment"/>.
    /// </summary>
    public double? SamplingRatio { get; set; }

    /// <summary>
    /// When true, an absent <see cref="SamplingRatio"/> defaults to 1.0 instead
    /// of failing the boot.
    /// </summary>
    public bool IsDevelopment { get; set; }

    /// <summary>
    /// Hosts whose outbound HTTP calls are CouchDB. Used to apply the
    /// ADR-0023 URL treatment, since CouchDB is indistinguishable from any
    /// other HTTP dependency at the instrumentation layer.
    /// </summary>
    public IList<string> CouchDbHosts { get; } = new List<string>();

    /// <summary>
    /// Redact document identifiers and view keys out of CouchDB URLs.
    /// Answered by demo phase D0 (QD2): if CouchDB document IDs are derived
    /// from applicant data this must be true, and telemetry must not be
    /// enabled against real data until it is.
    /// </summary>
    public bool RedactCouchDbUrls { get; set; } = true;

    /// <summary>
    /// ActivitySource names belonging to the application itself. NATS.Net's own
    /// source is registered automatically and does not belong here.
    /// </summary>
    public IList<string> ActivitySources { get; } = new List<string>();

    /// <summary>
    /// NATS.Net emits publish and subscribe spans and propagates trace context
    /// through message headers on its own, from version 3.0.1. Nothing injects
    /// traceparent by hand, and no custom message counters are needed.
    /// </summary>
    internal const string NatsActivitySourceName = "NATS.Net";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            throw new InvalidOperationException(
                "ServiceName is required (ADR-0006). Set a bare kebab-case name, " +
                "not a hostname or app pool name.");
        }

        if (string.IsNullOrWhiteSpace(ServiceNamespace))
        {
            throw new InvalidOperationException(
                "ServiceNamespace is required (ADR-0006).");
        }

        if (SamplingRatio is null && !IsDevelopment)
        {
            throw new InvalidOperationException(
                "SamplingRatio is not configured (ADR-0010). Absent sampler " +
                "configuration is a boot failure outside development, because a " +
                "silently wrong sampling rate is not detectable from the data.");
        }

        if (SamplingRatio is < 0 or > 1)
        {
            throw new InvalidOperationException(
                $"SamplingRatio must be between 0 and 1, was {SamplingRatio}.");
        }
    }

    internal double EffectiveSamplingRatio => SamplingRatio ?? 1.0;
}
