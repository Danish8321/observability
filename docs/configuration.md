# Configuration

`AddRaksawiObservability(...)` resolves its options in this order, each step
overriding the one before (ADR-0034):

1. Defaults.
2. The standard `OTEL_*` variables below.
3. The code lambda passed to `AddRaksawiObservability`.

Code wins. To let the environment decide something, don't set it in code. The
exception is `OTEL_SDK_DISABLED=true`: nothing in code can turn telemetry back
on.

The SDK is then configured explicitly from the resolved options, so no SDK
component re-reads a variable and contradicts this order.

## Supported variables

| Variable | Sets | Accepted values |
|---|---|---|
| `OTEL_SDK_DISABLED` | All signals off. Nothing is registered, and code cannot re-enable it | `true` or `false`, any case |
| `OTEL_SERVICE_NAME` | `ServiceName` | any non-empty value |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `OtlpEndpoint`. `v1/traces`, `v1/metrics` and `v1/logs` are appended to it, base path included | an absolute `http` or `https` URI |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | Nothing. The exporter is always http/protobuf | `http/protobuf` only. `grpc` is an error, because 4317 is closed estate-wide |
| `OTEL_TRACES_SAMPLER` | `SamplingRatio`. Counts as a sampler for ADR-0010 outside development | `always_on`, `always_off`, `traceidratio`, `parentbased_always_on`, `parentbased_always_off`, `parentbased_traceidratio` |
| `OTEL_TRACES_SAMPLER_ARG` | The ratio for the two `traceidratio` samplers. Defaults to `1.0` | a number in [0, 1], `.` as the decimal point |
| `OTEL_RESOURCE_ATTRIBUTES` | Extra resource attributes. The SDK reads it; `service.name` here loses to `ServiceName` | a `k=v,k=v` list |
| `OTEL_SERVICE_INSTANCE_ID` | `service.instance.id`, when code sets no `ServiceInstanceId` (ADR-0008) | any non-empty value |

Rules for every variable:

- An empty value is the same as unset.
- A value that doesn't parse stops the service at startup, with an error that
  names the variable. It is never replaced with a silent default.
- A `SamplingRatio` set in code is always parent-based
  (`parentbased_traceidratio`), even when the environment named a sampler that
  ignores the parent.

## Not supported

The package does not read these. Setting them has no effect, so don't assume the
SDK's own behaviour carries through:

- the per-signal `OTEL_EXPORTER_OTLP_{TRACES,METRICS,LOGS}_*` variables;
- `OTEL_EXPORTER_OTLP_HEADERS`;
- `OTEL_*_EXPORTER`;
- `OTEL_BSP_*`, `OTEL_BLRP_*` and `OTEL_METRIC_EXPORT_*` tuning, except
  `OTEL_METRIC_EXPORT_INTERVAL`, which the SDK still reads;
- attribute limits;
- `OTEL_METRICS_EXEMPLAR_FILTER` and `OTEL_PROPAGATORS`, which wait for the
  governance capability (plan contract C3).

There is also no `Observability` configuration section yet. Plan contract C2
puts it between the environment and code. Until it exists, a service that wants
appsettings to decide something reads the setting in its own lambda.
