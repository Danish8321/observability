# 34. The standard OTEL_* variables resolve before code

Date: 2026-11-19

## Status

Accepted. Covers only the environment step of the neutral plan's contract C2.
The `Observability` section, feature flags, `EnforcementMode` and the
performance budget, which the plan also gives this ADR, are not decided here.
They amend it when D6a/D6b reach them.

Deviates from the plan's order: this is part of D6b, built before D6a. It does
not deviate from Rev 3. Like ADR-0042, it was pulled forward by what circle
needed: circle-integration ticket 01, friction 3.

## Context

The package ignored every `OTEL_*` variable except `OTEL_SERVICE_INSTANCE_ID`.
An operator could not point a service at another collector, change its sampler
or switch telemetry off without a code change and a release.

The SDK reads some of these variables itself, but only where the package
doesn't set the value. The package always sets the endpoint, the protocol and
the sampler, so those variables were silently ignored. It also set the endpoint
by URI resolution, which drops a base path's last segment. `http://gateway/otlp`
exported to `http://gateway/v1/traces`.

## Decision

`AddRaksawiObservability` resolves defaults → `OTEL_*` → the code lambda. Code
wins. `docs/configuration.md` lists the variables read, their accepted values
and the ones deliberately not read.

- **Kill switch.** `OTEL_SDK_DISABLED=true` returns before anything is
  registered. Code cannot override it. This is the one exception to the order,
  and standard SDK semantics.
- **Strict parsing.** An empty value is unset. An unparsable value is a startup
  error naming the variable. This applies to `OTEL_SDK_DISABLED`, the endpoint,
  the protocol, the sampler and the sampler's argument. A silent default here
  is a service exporting somewhere nobody configured.
- **Protocol.** `OTEL_EXPORTER_OTLP_PROTOCOL` is validated, not used. Only
  `http/protobuf` passes; 4317 is closed estate-wide.
- **Sampler.**
  - All six standard names are accepted.
  - The three without `parentbased_` ignore the parent, as the specification
    says.
  - A `SamplingRatio` set in code is always parent-based, even over an
    environment sampler that wasn't. C2 maps the ratio to
    `parentbased_traceidratio`, and a code setting must not inherit a mode from
    the value it overrode.
  - An environment sampler satisfies ADR-0010's "no sampler outside
    development is a configuration error".
- **Endpoint path.** The signal path is appended to the endpoint, base path
  included.

The SDK is still configured explicitly from the resolved options. Nothing
downstream re-reads a variable and contradicts the order.

## Consequences

- An operator can redirect, resample or disable a service with environment
  variables alone, where that service's code doesn't already set the value. The
  samples set `SamplingRatio` in code, so their sampler is fixed. That is what
  "code wins" means, and a service that wants the environment to decide must
  leave the value unset.
- A typo in one of these variables now stops the service at startup. Before,
  it was ignored. Rev 3 I3.6 forbids failing a service start for telemetry; this
  is a configuration error, the same line `Validate()` already draws.
- **Evidence.**
  - `OtelEnvironmentTests` covers parsing and precedence. It also covers each
    sampler's decision on a root, a sampled parent and an unsampled parent; the
    endpoint the exporter is given; and provider registration with the switch
    on and off.
  - `e2e-instrumented.sh` starts payments-api with its endpoint only in
    `OTEL_EXPORTER_OTLP_ENDPOINT`. Its telemetry reaching the collector is the
    exported-behaviour proof for the endpoint.
- **Limit.** C2 asks for every check to be on exported telemetry, with an
  in-memory exporter. The test project deliberately has none (see
  `W3CTraceContextTests`). Sampler decisions are asserted on the sampler the
  pipeline builds, and the kill switch on the provider not being registered,
  rather than on exported spans. Only the endpoint is proven end to end.
