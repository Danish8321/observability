Status: proven — recipe run end to end; decision to record
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

### 2026-11-18 — writeToProviders proven on circle

**Change.** Circle `LoggingServiceCollectionExtensions.cs:66`, `UseSerilog(...)`
gains `writeToProviders: true`; nothing else. Branch
`spike/raksawi-observability`, uncommitted. Circle `check.sh`: build clean,
0 warnings.

**Run.** Circle on `:5068` (fake gateways, `TsoSpike` on `tso-spike-sql`,
placeholder Email/IFS credentials, email off) exporting to the
`raksawi-probe` stack. `GET /v1/accounts` x30, all 200.

**Read back from Loki** (`{service_name="TreasuryServiceOrchestrator"}`):
31 lines, where before there were none. 30 are the request-logging line, one
is ASP.NET Core's https-redirect warning. The body is the template
`HTTP {RequestMethod} {RequestPath}{QueryString} responded {StatusCode} in {Elapsed:0} ms`,
not rendered text, so ADR-0004 holds. `trace_id` and `span_id` are on every
line, so logs link to their traces. No Serilog property reached Loki.

**Allowlist applied** (`raksawi_telemetry_attributes_dropped` in Mimir, by
`attribute_key`): every Serilog property was dropped in-process, including
`RequestPath` 31, `RequestMethod` 30, `StatusCode` 30, `Elapsed` 30,
`QueryString` 30, `SourceContext` 37, `Application` 37, `CorrelationId` 31,
`RequestId` 31, `ConnectionId` 31, `TraceId`/`SpanId`/`ParentId` 31 each,
`{OriginalFormat}` 37, and `db.query.text` 733 from the SqlClient spans.

**What this costs an adopter:**

- The log lines arrive with no queryable fields. The method, path and status
  of a request log are dropped until declared. `CorrelationId` is circle's
  own name for what the estate calls `correlation.id`. Declaring keys runs
  into the ADR-0017 signed-pack friction from issue 01.
- `TraceId` and `SpanId` from Serilog's span enricher are redundant: the
  record already carries `trace_id` and `span_id`. Dropping them costs
  nothing.
- `writeToProviders` forwards to every registered `ILoggerProvider`, not
  only OpenTelemetry. Circle's console then printed each event twice, once
  as Serilog JSON and once in the Microsoft console format (`info: ...`),
  from the default providers `WebApplication.CreateBuilder` adds. An adopter
  needs `builder.Logging.ClearProviders()` *before*
  `AddRaksawiObservability(...)`. Proven the same day (below).

**Left for the decision:** option 1 needs no package change, so it needs no
ADR. The adoption recipe still needs writing down: `writeToProviders: true`,
clearing the default providers in that order, and which Serilog keys to
declare (request method, path and status at least). Whether that recipe
lives in `samples/README.md` or an adopter guide, and which keys to declare,
is the owner's call.

### 2026-11-18 — ClearProviders ordering proven

Circle `LoggingServiceCollectionExtensions.cs:53` gains
`builder.Logging.ClearProviders();` immediately before `UseSerilog(...)`.
`AddSerilogLogging()` runs before `AddObservability()` (`Program.cs:14-15`),
so the package's OpenTelemetry provider is registered after the clear and
survives it. Circle `check.sh`: 0 warnings, 0 errors.

Same run as above, `GET /v1/accounts` x30, all 200:

- Console: 0 lines in the Microsoft format, 217 Serilog JSON lines. The
  doubling is gone.
- Loki: 31 lines from the run window (30 request lines and the https
  warning), the same count as without the clear.

So the adoption recipe for a Serilog service is two lines, in this order,
with the Serilog setup before `AddRaksawiObservability(...)`:

```csharp
builder.Logging.ClearProviders();
builder.Host.UseSerilog((ctx, _, cfg) => { ... }, writeToProviders: true);
```

Still open: where the recipe is written down, and which Serilog keys to
declare.
