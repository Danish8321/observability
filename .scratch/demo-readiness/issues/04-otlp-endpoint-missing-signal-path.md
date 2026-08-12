Status: closed

# OTLP exports 404 — ConfigureOtlp sets Endpoint programmatically, which disables the SDK's auto-append of `/v1/traces` / `/v1/metrics`

`src/Raksawi.Observability/RaksawiObservabilityExtensions.net10.cs:84-90` (and
the net48 counterpart, same pattern):

```csharp
private static void ConfigureOtlp(OtlpExporterOptions otlp, RaksawiObservabilityOptions options)
{
    otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
    otlp.Endpoint = options.OtlpEndpoint;
}
```

The OpenTelemetry .NET SDK (1.17.0, pinned in `Directory.Build.props`) only
appends the per-signal path (`v1/traces`, `v1/metrics`, `v1/logs`) to the OTLP
HTTP endpoint when the endpoint comes from the **default or an environment
variable** (`OTEL_EXPORTER_OTLP_ENDPOINT`). Setting `Endpoint` programmatically
— which this line always does, since `RaksawiObservabilityOptions.OtlpEndpoint`
always has a value — opts out of that auto-append. The SDK then POSTs straight
to the bare endpoint (e.g. `http://localhost:4319/`), which is not a route the
collector's OTLP HTTP receiver serves.

## Impact

Every export, on every service that calls `AddRaksawiObservability()`, 404s
silently — traces and metrics never reach the collector, and nothing in the
application logs it as an error (the OTLP HTTP exporter logs failures at
`Information`, not `Error` or `Warning`, so it does not surface without
`Logging:LogLevel:System.Net.Http.HttpClient` turned up). This is **not**
specific to this demo's docker-networking rework — the bug is in the
mechanism package, so it would 404 identically against a correctly-configured
SigNoz/collector on the documented port 4318.

Confirmed live 2026-08-11: `Screening.Api` and `Screening.Worker` both logged
`POST http://localhost:4319/` → `404` for every trace and metric export
during a full happy-path run (`POST /applications` → CouchDB write → NATS
publish → worker consume → CouchDB update → `screened`). The business flow
completed correctly; the telemetry for it never left the process.

## Fix

Append the signal path explicitly rather than relying on the SDK's
opt-in-only auto-append, e.g.:

```csharp
otlp.Endpoint = new Uri(options.OtlpEndpoint, "v1/traces");   // per AddOtlpExporter overload's signal
```

or construct the endpoint per-signal inside the `WithTracing`/`WithMetrics`
`AddOtlpExporter` calls instead of sharing one `ConfigureOtlp`, since traces
and metrics need different suffixes.

## Fixed (2026-08-11)

`ConfigureOtlp` now takes an explicit `signalPath` parameter, applied as
`otlp.Endpoint = new Uri(options.OtlpEndpoint, signalPath)`. Both call sites
(`WithTracing`/`WithMetrics`) pass `"v1/traces"` / `"v1/metrics"`
respectively. Applied identically in both
`RaksawiObservabilityExtensions.net10.cs` and `RaksawiObservability.net48.cs`.

Verified: `check.sh` clean (build + format), `test-fast.sh` 35/35. Live: both
`Screening.Api` and `Screening.Worker` logs show
`POST http://localhost:4319/v1/traces` → `200` and `.../v1/metrics` → `200`.
ClickHouse confirms spans arriving for both services.

## Comments
