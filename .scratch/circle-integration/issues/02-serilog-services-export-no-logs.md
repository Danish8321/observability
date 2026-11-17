Status: open — needs a decision
Priority: high

# A service that logs through Serilog exports no logs

Found 2026-11-17 in the circle spike (issue 01). Circle with
`AddRaksawiObservability(...)` exported traces and metrics, and Loki holds
zero log lines for it over 24 h. Circle uses `Serilog.AspNetCore`, and
`UseSerilog` replaces the `ILogger` providers, so the OpenTelemetry provider
the package registers (`RaksawiPipeline.AddRaksawiLogging`) receives nothing.
The cause is inferred from that; a run with Serilog removed would prove it.

Nothing fails and nothing warns. A service on Serilog gets traces and
metrics, and the log signal is silently absent.

## Options

- `UseSerilog(..., writeToProviders: true)` in each service: no package
  change, but every adopter has to know it, and Serilog's own properties
  then pass through the log allowlist (ADR-0028) under Serilog's names.
- A Serilog sink to OTLP: a new dependency in the estate, and a second log
  path that does not go through `LogAllowlistProcessor`, which ADR-0028
  exists to prevent.
- Document it as unsupported: honest, but the estate's most common logging
  setup is then outside the package.

## Done when

A decision is recorded (ADR if it changes the package), and a Serilog
service's logs are read back from Loki in the local stack with the allowlist
applied.

## Comments

### 2026-11-17 — what writeToProviders forwards

From the Serilog.Extensions.Logging source (`LoggerProviderCollectionSink.Emit`,
`SerilogLogValues`): with `writeToProviders: true`, each provider is called
with `SerilogLogValues`, an `IReadOnlyList<KeyValuePair<string, object?>>`
holding every event property (scalars unwrapped to their raw value,
structures and sequences left as Serilog values) plus `{OriginalFormat}`
with the template. That is the state shape `ILogger` itself produces, so
`LogAllowlistProcessor` sees Serilog properties as attributes and filters
them like any other, and the body is the template, not rendered text
(ADR-0004 holds).

So option 1 needs no package change and no dependency. Expected cost:
enricher properties (`SourceContext`, `RequestId`, `MachineName`, span
enrichers) are undeclared and dropped in-process, counted by
`raksawi.telemetry.attributes.dropped`. That is the allowlist working;
which of them deserve declaring is a governance question.

Proof pending: set it in circle and read the logs back from Loki.
Blocked on reading circle's source in this session.
