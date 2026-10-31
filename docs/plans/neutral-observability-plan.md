# Plan: business-neutral observability core + compliance add-on

Revision 7 — 2026-10-30. Revision 6's content, cut into production deliverables D1–D12 (section "Production deliverables"). The milestones M0–M10 below remain the description of the work; the deliverables decide its order and what ships when.

**Status: accepted for delivery, 2026-10-30.** ADRs 0032–0040 are not written yet; each is written in the deliverable that first needs it, and until then the existing ADRs remain authoritative.

## Context

Today `Raksawi.Observability` is "mechanism" in name only: it hard-codes KYC
vocabulary (`application.id` in `AllowlistRules.NeverAMetricDimension` and in the
collector regexes), always registers the NATS.Net source, threads CouchDB host
knowledge through the allowlist, enforces estate rules (namespace required,
sampler required) that standard OpenTelemetry does not, ignores `OTEL_*`
variables, and exports to SigNoz. A new project that uses neither NATS nor
CouchDB and is not KYC cannot adopt it cleanly.

Goal of the first release:

> A fresh .NET 10 application enables standard OpenTelemetry with one line
> (`builder.AddRaksawiObservability();`), while a regulated service opts into
> integrations and enforceable telemetry governance without contaminating core.

Both paths are proven by clean consumer projects built from the packed NuGet
artifacts. Phase 1 is .NET 10; phase 2 is .NET Framework 4.8.

Posture sentence, used verbatim in README, onboarding docs, package
descriptions and ADR-0032: *Core provides observability. Compliance provides
governance. `Raksawi.Observability.Compliance` is REQUIRED for services handling
regulated customer data.*

## Decisions settled

| # | Decision |
|---|---|
| Q1 | Three tiers: **core** → **Compliance** engine → **Kyc** vocabulary pack. `Raksawi.Observability.*` prefix kept |
| Q2 | Core alone = plain OpenTelemetry defaults, no in-process filtering. Collector policy stays mandatory for any regulated estate. ADR-0032 records ADR-0003 reversed for core-only services |
| Q3 | Flags are options bound from `IConfiguration` (`Observability:*`), read once at startup |
| Q4 | Compliance has an explicit `EnforcementMode` (`Required` / `Warn` / `Disabled`), default `Required` in every environment, no environment-name detection; non-`Required` is always reported. Regulated services additionally declare `Governance:Required` (contract C3) |
| Q5 | Packages are backend-agnostic (OTLP only). `deploy/` moves to Grafana LGTM; ADR-0037 records choosing it ahead of the Rev 3 I3.11 bake-off |
| Q6 | Honour standard `OTEL_*` variables; parameterless `AddRaksawiObservability()`; estate rules behind `StrictEstateProfile` |
| Q7 | NATS and CouchDB are separate packages |
| Q8 | Phase 2 multi-targets the same packages again |
| Q9 | Integrations plug in through explicit calls (`.AddNats()`, `.AddCouchDb(...)`), each also flag-gated |
| Q10 | CouchDB URL redaction belongs to the CouchDb package; Compliance admits `url.full`/`url.query` only on a verified redaction (contract C5) |
| Q11 | Key declarations (`AllowedAttributeKeyAttribute`, `DataClass`) move to core as inert metadata |
| Q12 | Detection is in phase 1: alerting, dead man's switch, tail sampling, Grafana correlations and runbooks (M9) right after the minimal Grafana backend, on today's code; async pipeline, SLOs and the service register (M10) also in phase 1 |
| Q13 | Alerts are delivered by **email** (Grafana SMTP contact point). Demo uses a local mail catcher so alerts are visible and testable without a real mail server |
| Q14 | Alert thresholds: SLO-based (multi-window burn rate) as the end state in M10; fixed starting thresholds in M9 until then (table in M9). Both are starting values, reviewed after two weeks of real traffic |
| Q15 | Exemplars stay **on** (`trace_based`) with their attributes stripped, reversing revision 5's `always_off`, so a latency spike links to its trace |

## Production deliverables

### Starting point (2026-10-30)
- Nothing is in production: no collector, no store, no service on the package.
- One release line. Packages ship as `2.0.0-preview.N` until 2.0.0; there is no 1.x to maintain.
- Production is **EKS**. Object storage is **S3** (one bucket per store, IAM roles for service accounts, no access keys in the cluster). Alert email goes through **Amazon SES** as Grafana's SMTP server; any SMTP relay can replace it.
- **Infra owns the platform** (R6, 2026-10-31): EKS, S3, IAM, SES and Grafana are operated by the Infra team, and this repo has no access to them now or later. This repo writes the platform-side configuration as files and verifies it locally; Infra deploys it.
- The only NATS service is the **screening worker** the sample models, on **.NET 10**, using core NATS publish-subscribe. JetStream is in development and not available.
- The new project goes to production with 2.0.0. There is no staging environment; Infra decides whether to create one. This repo's stand-in is the **local stack**: `deploy/docker-compose.yaml` running the collector, stores, Grafana and the Screening services on the preview packages.

### What counts as a deliverable
Each one: (1) for a package, is packed as a prerelease and running in the local stack; for platform configuration, is a file in `deploy/` that the local stack runs unchanged, plus a handoff note for Infra; (2) is useful if nothing after it ships; (3) can be rolled back on its own; (4) passes the checks that apply (`check.sh`, unit tests, e2e where Docker exists); (5) is at most about two engineer-weeks. A deliverable writes the ADRs it needs; there is no separate decision milestone.

### Decisions made while cutting
| # | Decision |
|---|---|
| R1 | ADR-0007 is amended: `X-Session-Id` carries `session.id`, `X-Correlation-Id` carries the workflow `correlation.id`, as a stated deviation from Rev 3 D2.13. The missing GUID validation of the session header (ADR-0007, N-D7) is a bug fixed in D1 |
| R2 | One collector gateway replica in production for now; documented single point of failure, covered by the dead man's switch and the persistent queue. Two-tier, trace-routed collectors are parked until load requires them |
| R3 | Each pod sets `OTEL_SERVICE_INSTANCE_ID` from its pod name; the collector adds Kubernetes metadata (namespace, pod, node), all inside the allowed resource families |
| R4 | Core NATS: visibility comes from broker metrics and in-process traces. Panels 3.1, 3.3 and 3.6 are marked "needs JetStream"; JetStream visibility is parked until JetStream exists in the service |
| R5 | Staging telemetry goes to the production stack tagged `deployment.environment.name=staging`; alert rules ignore staging except where a deliverable says otherwise |
| R6 | Infra owns the platform. Wherever a deliverable below said "production" or "staging", this repo's part ends at configuration files verified in the local stack and a handoff note in `deploy/HANDOFF.md`; deploying them, and the production checks (restore from S3, real SES email, MTTR baseline), are Infra's. R2–R5 stand as recommendations to Infra |

### The deliverables

| # | Deliverable | Draws on | Lands in | Done when | Size |
|---|---|---|---|---|---|
| D1 | CouchDB URL leak fix (C5 verification record); session-header GUID check; header ADR (R1) | M1 | `2.0.0-preview.1` | failing-first tests pass on exported telemetry; ADR merged | ~1 wk |
| D2 | Platform configuration for Infra: collector gateway (persistent queue, self-scrape, k8s metadata), Tempo/Loki/Mimir with S3 settings, Grafana data sources; ADR-0037 (Grafana ahead of the bake-off) | M6-min | `deploy/` + handoff | the local stack runs the same files; data sources healthy; a synthetic OTLP payload is queryable in all three stores | ~1.5 wk |
| D3 | Alerting as code: the four dead-man's-switch alerts, collector queue, daily heartbeat, SMTP contact point; runbooks for each; ADR-0040 (part 1) | M9 | `deploy/` + handoff | in the local stack, stopping the collector, its exporter and a store each delivers its own email to a local SMTP sink; heartbeat arrives | ~1.5 wk |
| D4 | Local stack running Screening API, worker, NATS and CouchDB on preview packages; tail sampling (ADR-0039); Grafana trace↔log↔metric links; exemplars; service alerts (abandoned, error rate, latency, screening slow) | M9 | `deploy/` + handoff | each demo fault produces its email locally; error and abandoned traces kept at 100% under a 10% base rate; an exemplar opens its trace | ~2 wk |
| D5 | NATS publish-subscribe visibility: exporter configuration, broker panels, slow-consumer and delivery-gap alerts | M6, M9 | `deploy/` + handoff | slow consumer induced locally emails; panels 3.2 and 3.4 show data or are recorded as not available on core NATS | ~1 wk |
| D6a | Neutral core, vertical slice: one-line registration, ASP.NET Core instrumentation, OTLP export, `Neutral.Api`, local-feed smoke; ADR-0032 | M2 | `preview.2`, local stack | `Neutral.Api` exports in the local stack with no NATS, CouchDB or compliance code loaded | ~1.5 wk |
| D6b | Core contracts: C1 contributions and stages, C2 configuration resolution and the `OTEL_*` table, resource identity, governance capability; ADRs 0034, 0035, 0038 | M2 | `preview.3`, local stack | every C1/C2 test passes on exported telemetry; contracts frozen | ~1.5 wk |
| D7 | NATS and CouchDB integration packages; ADR-0033 | M3 | `preview.4`, local stack | Screening runs on them in the local stack; mixed-case host and rewrite cases never export a raw URL | ~1.5 wk |
| D8a | Compliance add-on, spans and logs: catalog, processors, enforcement mode, `Governance:Required`, baggage off | M4 | `preview.5`, local stack | Screening in the local stack runs `Required`; governance fails startup when uncovered; PII canary absent | ~1.5 wk |
| D8b | Compliance surfaces: span events and links, exemplar safety, analyzer packed in the nupkg; KYC vocabulary pack; Screening migrated; surface table published | M4, M7 | `preview.6`, local stack | every surface-table row Verified or an accepted limitation; analyzer fires in a clean consumer | ~1.5 wk |
| D9a | Collector policy generator: catalog, three input adapters, reproduces today's hand-written policy byte for byte; ADR-0036 | M5 | `deploy/` + handoff | generated file equals the current one; contract tests switched | ~1.5 wk |
| D9b | Generated policy goes semantic: OTLP fixture tests, metric re-aggregation table, exemplar attributes, the ADR-0004 log-body scan | M5 | `deploy/` + handoff | every merge-table row passes on value assertions; canary absent after the collector | ~1.5 wk |
| D10 | SLOs for screening-api and screening-worker, burn-rate alert rules defined and exercised on replayed fixtures; ADR-0040 (part 2) | M10 | `deploy/` + handoff; Infra tunes after go-live | rules evaluate correctly on fixtures; targets documented as provisional | ~1.5 wk |
| D11 | Service register file and the coverage panel; "service stopped reporting" alert reads it; freshness probe | M10 | `deploy/` + handoff | stopping a registered service in the local stack emails within 5 minutes | ~1 wk |
| D12 | **2.0.0**: packaging metadata, central package management, clean consumers from the packed nupkg, migration notes; complete handoff to Infra | M8 | `2.0.0` + handoff | both clean consumers pass; every alert has been seen to fire at least once in the local stack; Infra has accepted the handoff | ~1.5 wk |

Total about 23 engineer-weeks (estimate, ±40%), the same work as revision 6 cut differently.

### Order
D1 → D2 → D3 → D4 → D5 → D6a → D6b → (D7 ∥ D8a ∥ D9a) → D8b → D9b → D10 → D11 → D12.
- D2–D5 need no package change, so detection exists before the refactor starts.
- D6b freezes the contracts D7, D8a and D9a build on; they start only after it.
- D9a may start as soon as D6b lands, because it first only has to reproduce today's file.
- From D4 onward, every deliverable keeps every existing alert green in the local stack.
- The local stack needs a running Docker daemon; `e2e*.sh` fail rather than skip without one.

### Parked
- JetStream visibility (oldest message age, backlog, DLQ), when JetStream exists in the screening worker.
- Two-tier, trace-routed collectors, when one gateway is no longer enough.
- Phase 2, .NET Framework 4.8.

## Target package shape

```
Raksawi.Observability                  core (neutral)
├─ Raksawi.Observability.Nats          NATS.Net source, messaging.* declarations, broker-scrape collector fragment
├─ Raksawi.Observability.CouchDb       changes-feed filter, URL redaction, couchdb.* declarations
└─ Raksawi.Observability.Compliance    allowlist engine, processors, drop metrics, enforcement mode,
   │                                   analyzer (packed inside), collector policy generator
   └─ Raksawi.Observability.Kyc        application.id, screening.*, application.found/status, KycTelemetry
```

Integrations and Compliance depend on core only; Kyc depends on Compliance;
nothing depends on an integration. Optional means optional at run time too: a
core-only process registers no NATS source, no CouchDB hooks, no compliance
processor and no compliance meter (asserted by `samples/Neutral.Api`).

## Contracts — resolved in M0, before any M2 code

### C1. Pipeline composition and lifecycle

- **One provider per signal, owned by the library.** `AddRaksawiObservability`
  calls `services.AddOpenTelemetry()` once and configures it through
  `WithTracing` / `WithMetrics` / `WithLogging`. Everything happens at provider
  *configuration* time; nothing touches a built provider.
- **Signal-aware contributions.** `IRaksawiObservabilityContribution` has an
  `Id` (for duplicate detection) and three optional members with default no-op
  bodies: `ConfigureTracing(TracerProviderBuilder, TracingStage)`,
  `ConfigureMetrics(MeterProviderBuilder, MetricsStage)`,
  `ConfigureLogging(LoggerProviderBuilder, LoggingStage)`. A contribution
  implements only the signals it affects. A disabled signal skips only that
  signal's members — NATS still registers its meters when traces are off.
- **Stages per signal.**
  - Tracing: `Sources` → `Instrumentation` → `Enrich` → `Process` → `Filter` → exporter.
  - Logging: `Process` → `Filter` → exporter.
  - Metrics: `Instruments` → `Views` → reader/exporter. There is no metric
    `Filter`: the SDK's view `TagKeys` is an include-list, so deny-by-key of
    Class 2 dimensions stays at the analyzer and collector (to be re-verified
    against OpenTelemetry 1.17 in M2; if a deny form exists, Compliance uses it).
- **Ordering guarantee.** Contributions are collected first and applied only
  when the provider is configured: grouped by stage (stage order decides
  execution), and in registration order within a stage (deterministic ties).
  The OTLP exporter is added after the last stage. Because OTel runs processors
  in registration order, every `Filter` processor sees what every earlier stage
  set. Tests:
  - a later-stage contribution registered *first* and an earlier-stage one
    registered *second* execute in stage order;
  - three contributions in one stage execute in registration order;
  - a `Process` contribution registered after Compliance still runs before it.
- **What a filter guarantees.** It prevents *export* of what it removes. It
  does not prevent a value from existing in process memory or being observed
  by other listeners. Docs say exactly this.
- **Duplicate registration.** A second `AddRaksawiObservability` call with no
  options is a no-op; with conflicting options it throws at startup. A second
  `.AddNats()` (same `Id`) with identical options is a no-op; with different
  options it throws.
- **Foreign OTel registration.** The supported composition is through the
  Raksawi builder only. Contract: *supported configuration cannot create an
  unfiltered export path.* A foreign `AddOpenTelemetry().WithTracing(...)`
  registered **after** `AddRaksawiObservability` runs after the `Filter` stage
  and so exports filtered data. One registered **before** it could precede the
  filter; `AddRaksawiObservability` therefore inspects the service collection
  for an existing OpenTelemetry provider registration and, under
  `Governance:Required`, fails startup (otherwise logs a warning). The
  detection mechanism (public `IConfigureTracerProviderBuilder` /
  `IConfigureLoggerProviderBuilder` descriptors) is verified against
  OpenTelemetry 1.17 in M2. RKS003 remains a build-time aid, not the control.
- **Shutdown.** Providers are owned by DI and disposed by the host; disposal
  calls `ForceFlush` within the host shutdown timeout. No lossless-delivery
  claim is made in-process (the collector's persistent queue is the durability
  point). Test: pending spans are exported on host stop.

### C2. Configuration resolution

One explicit resolution step produces `ResolvedObservabilityOptions`, which is
validated and is the *only* input to provider configuration:

1. Defaults.
2. Standard `OTEL_*` variables mapped in: `OTEL_SERVICE_NAME`,
   `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_EXPORTER_OTLP_ENDPOINT`,
   `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_TRACES_SAMPLER`,
   `OTEL_TRACES_SAMPLER_ARG`, `OTEL_SDK_DISABLED`.
3. `Observability` section (appsettings and `Observability__*` environment, in
   normal `IConfiguration` order).
4. Code lambda.

Supported environment variable contract (published as a table in
`docs/configuration.md`; anything not listed is **not** read by the resolver
and is documented as unsupported, so no one assumes SDK behaviour carries
through):

| Variable | Option | Scope | Precedence | Validation |
|---|---|---|---|---|
| `OTEL_SDK_DISABLED` | all signals off | global | overrides everything | `true`/`false`, else startup error |
| `OTEL_SERVICE_NAME` | `ServiceName` | resource | step 2 | non-empty; kebab-case under strict profile |
| `OTEL_RESOURCE_ATTRIBUTES` | extra resource attributes | resource | step 2; `service.name` here loses to `OTEL_SERVICE_NAME` (SDK rule) | `k=v` list, percent-decoded; malformed pair is a startup error |
| `OTEL_SERVICE_INSTANCE_ID` | `ServiceInstanceId` | resource | step 2 | non-empty |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `Otlp:Endpoint` | all signals (per-signal path appended) | step 2 | absolute http(s) URI |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `Otlp:Protocol` | all signals | step 2 | `http/protobuf` only in phase 1; `grpc` is a startup error (4317 closed estate-wide) |
| `OTEL_TRACES_SAMPLER`, `OTEL_TRACES_SAMPLER_ARG` | `Sampling` | traces | step 2 | the six standard sampler names; ratio in [0,1] |
| `OTEL_METRICS_EXEMPLAR_FILTER` | exemplar filter | metrics | step 2; default `trace_based` | standard values; under `Governance:Required`, `always_on` is a startup error (records exemplars for unsampled measurements, no diagnostic use) |
| `OTEL_PROPAGATORS` | propagators | process | step 2, **overridden by Compliance** (C3) | under `Governance:Required`, a list naming `baggage` is a startup error |

Explicitly unsupported in phase 1: per-signal `OTEL_EXPORTER_OTLP_{TRACES,METRICS,LOGS}_*`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_*_EXPORTER`, `OTEL_BSP_*`/`OTEL_BLRP_*`/`OTEL_METRIC_EXPORT_*` tuning, attribute limits.

Rules:
- Empty string = unset. Explicit `false` is a value. An unparsable value is a
  startup validation error naming the key, never a silent default.
- The SDK is then configured **explicitly** from the resolved options
  (endpoint, protocol, sampler, resource), so no SDK component re-reads an
  environment variable and contradicts the documented precedence.
- `OTEL_SDK_DISABLED=true` is a **global kill switch and the one exception to
  the precedence order**: it disables all three signals and cannot be
  re-enabled by any section or code setting (standard SDK semantics, kept
  deliberately). It is applied by the resolver, not left to the SDK, and is
  tested on exported telemetry (nothing exported even with
  `Traces:Enabled = true` in code). Under `Governance:Required` a killed SDK is
  valid — nothing is exported, so nothing is unfiltered — and is reported in
  the startup diagnostics.
- Tests assert on what is **exported** (in-memory exporter: resource attributes,
  sampler decisions, endpoint actually used), not on the options object.

Resource identity (amends ADR-0006/0008 only where stated):
| Attribute | Source | Strict profile |
|---|---|---|
| `service.name` | resolved option → `OTEL_SERVICE_NAME` → entry assembly name | required, kebab-case |
| `service.namespace` | option / `OTEL_RESOURCE_ATTRIBUTES` | required |
| `service.version` | option → entry assembly informational version | — |
| `service.instance.id` | option → `OTEL_SERVICE_INSTANCE_ID` → `machine:service` (ADR-0008, stable across recycle, no PII) | containers and multi-replica hosts must supply it |
| `deployment.environment.name` | option / `OTEL_RESOURCE_ATTRIBUTES` | — |

`machine:service` is unique only when a host runs one instance of a service
(IIS app pool, single-instance VM). Containers and any host running replicas
must set `OTEL_SERVICE_INSTANCE_ID` (compose/orchestrator supplies it); the
strict profile warns when the derived fallback is used inside a container
(`DOTNET_RUNNING_IN_CONTAINER`). ADR-0035 records the assumption against the
ADR-0024 working position rather than assuming global uniqueness.

Sampling: unset → `OTEL_TRACES_SAMPLER(_ARG)` → `parentbased_always_on`.
`Sampling:Ratio` maps to `parentbased_traceidratio`. Strict profile requires a
sampler outside Development (ADR-0010). With tail sampling at the collector
(M9), Raksawi services sample `parentbased_always_on` (ratio 1.0) and the
retention decision moves to the collector policy; ADR-0039 amends ADR-0010 so
the required, explicit configuration is the collector's tail policy, while the
service still must state its head sampler explicitly under the strict profile. `TraceContext:EnforceW3C` only forces
the W3C id format (`ServiceIdentity.EnsureW3CTraceContext`); it does not affect
propagation or sampling.

Feature flags (documented in `docs/configuration.md`):

| Category | Key (`Observability:…`) | Default | Meaning |
|---|---|---|---|
| Signal | `Traces:Enabled`, `Metrics:Enabled`, `Logs:Enabled` | true | provider registered or not |
| Instrumentation | `Instrumentation:AspNetCore`, `:HttpClient`, `:Runtime` | true | that instrumentation registered; app's own sources unaffected |
| Integration | `Integrations:Nats:Enabled`, `Integrations:CouchDb:Enabled` | true once added | integration configures itself |
| Integration settings | `Integrations:CouchDb:Hosts`, `:SkipChangesFeed` | [] / true | host match case-insensitive; entries containing `:` or `/` rejected |
| Policy | `Compliance:EnforcementMode`, `Compliance:Signals:Traces:EnforcementMode`, `Compliance:Signals:Logs:EnforcementMode` | `Required` / inherit / inherit | global default, per-signal override; see C3 |
| Estate | `StrictEstateProfile`, `Governance:Required` | false / false (both true for Raksawi services) | estate conventions; governance contract |
| Export | `Otlp:Endpoint`, `Otlp:Protocol` | `http://localhost:4318`, http/protobuf | |
| Trace context | `TraceContext:EnforceW3C` | true | |

Safety interlock: CouchDB URL redaction has **no** off switch and is not tied to
`Instrumentation:HttpClient`. If HttpClient instrumentation is off there are no
HTTP spans to redact; if it is on and CouchDb is added, redaction runs. Estate
conventions (`StrictEstateProfile`) and security controls (`Compliance:*`,
`Governance:Required`) never share a switch.

Startup diagnostics: one structured log record listing enabled signals,
registered instrumentations, integrations (by `Id`), compliance mode per
signal, exporter protocol and endpoint with userinfo and query stripped,
strict-profile and governance flags. Never values that could carry data.

### C3. Compliance surfaces and guarantees

Four layers, each documented as defence in depth, none as a substitute for the next:
1. **Instrumentation discipline** — guidance plus analyzer (static, literal keys only).
2. **Compliance package** — in-process allowlist before export.
3. **Collector policy** — generated, fail-closed, the only control for agent-instrumented services (ADR-0009).
4. **Backend access and retention** — ADR-0020 tiers; outside these packages.

Surface table (published as `docs/compliance-surfaces.md`, and a **release
gate**: the published version adds, per row, the test that proves it, the known
limitation, and a status of *Not implemented* / *Implemented* / *Verified*; a
release requires every row to be *Verified* or to carry an accepted, documented
limitation). Current state from reading the code is in brackets:

| Surface | In-process (Compliance) | Collector |
|---|---|---|
| Span attributes | allowlist [yes] | allowlist [yes] |
| Span **event** attributes (incl. exception events) | allowlist — **new** [not filtered] | allowlist on `spanevent` context — **new** [not filtered] |
| Span link attributes | allowlist — **new** [not filtered] | allowlist — **new** [not filtered] |
| Resource attributes | built by core only (ServiceIdentity) | allowlist [yes] |
| Log attributes | allowlist [yes] | allowlist [yes] |
| Log body | not filterable; interpolation banned by analyzer (ADR-0004) | pattern scan — **new** [ADR-0004 and `docs/allowlist.md` describe one; `config.yaml` contains none] |
| `exception.message` / stacktrace | kept (ADR-0004) | pattern scan — **new**, same gap |
| Metric dimensions | analyzer RKS002 (literals); no runtime deny (see metric governance) | Class 2 removal with re-aggregation — **changed** [delete only] |
| Baggage | **new**: baggage propagation disabled process-wide (the "baggage stays disabled" rule in `KycTelemetry.cs` is currently a comment, not code) | n/a |

Log bodies and exception text: pattern scanning is a **compensating control**.
It cannot recognise every sensitive value; preventing emission (structured
logging, ADR-0004, analyzer) is the primary control and the docs say so.

Baggage, precisely: two propagators are in play on .NET 10 — OpenTelemetry's
`Propagators.DefaultTextMapPropagator` and `System.Diagnostics`'
`DistributedContextPropagator.Current`, which ASP.NET Core and HttpClient use
for inbound extraction and outbound injection. Compliance sets both to
trace-context-only. This is process-wide, not limited to Raksawi activities —
stated in ADR-0032. M4 verifies which of the two carries baggage on .NET 10 and
for NATS.Net headers, with tests for inbound and outbound W3C trace context
(still propagated) and baggage (not propagated), including with another
OpenTelemetry component registered.

Metric governance (no `EnforcementMode` for metrics; it would imply span-level
protection that does not exist):
- **Prevent at source:** RKS002 coverage is documented exactly — which
  `System.Diagnostics.Metrics` instrument APIs and argument shapes it inspects
  (literal keys, `KeyValuePair`, `TagList` initialisers, params arrays) and what
  it cannot see (keys in variables, third-party instrumentation).
- **Collector:** removing a dimension must re-aggregate, or two data points that
  differed only by the removed key become duplicate series the backend rejects
  or overwrites. M5 chooses the OTTL form the pinned collector supports and
  proves with fixtures, per instrument type (counter, up-down counter,
  histogram, gauge), that the exported series is correct.
- **Stated limit:** collector removal does not undo cardinality already paid in
  the SDK's in-memory aggregation; the SDK's cardinality limit bounds that, and
  the docs name it as a limit, not a control.
- Re-verified in M2: if OpenTelemetry 1.17 offers a deny-form view, Compliance
  uses it and this section changes.
- **Exemplars (P0, revised in revision 6).** An exemplar can carry the
  measurement's attributes that a view filtered out of the data point, plus
  trace and span ids. Compliance registers no attribute-dropping views, so its
  exemplars carry only the ids, which is what Grafana needs to link a metric
  point to its trace. Exemplars therefore stay **on** (`trace_based`), and:
  - under `Governance:Required`, Compliance rejects startup if any registered
    view restricts `TagKeys` (the only way filtered attributes reach an
    exemplar), so exemplar attributes are empty by construction in-process;
  - the collector removes exemplar attributes for every metric as a second
    control; whether the pinned collector's OTTL can address them is verified
    in M9 (moved forward from M5). Fallback if it cannot: in-process control
    alone for our services, and exemplars dropped at the collector only for
    agent-instrumented services — recorded as a known diagnostic loss there. Regression test: a Class 2 attribute recorded on a
  measurement, with a sampled trace active, appears in neither the exported
  data point nor any exemplar — in-process and after the collector.
- **Re-aggregation semantics.** After removing a dimension, points that become
  identical within one metric, one scope and one resource are merged as:

  | Metric type | Merge | Preconditions; otherwise |
  |---|---|---|
  | Sum, monotonic (counter), delta | values summed | — |
  | Sum, monotonic, cumulative | values summed, earliest start time | all points share start time; otherwise the metric stream is dropped and counted |
  | Sum, non-monotonic (up-down counter) | values summed, temporality kept | as above for cumulative |
  | Histogram | bucket counts, count and sum added; min of mins, max of maxes | identical bucket boundaries; otherwise dropped and counted |
  | Exponential histogram | not merged | dropped and counted |
  | Gauge | not merged — there is no correct merge for a last value | dropped and counted |

  A Class 2 key on a gauge or exponential histogram is a governance
  violation to surface, not something to repair: the collector drops that
  stream and increments a counter on its own telemetry (ADR-0031 self-scrape),
  so the violation is visible on the dashboard. Temporality and monotonicity
  are never changed by the policy. Fixtures assert resulting values, point
  counts, start times, temporality and bucket contents — not only the absence
  of the key.

Enforcement contract:
- `EnforcementMode` resolution: `Compliance:EnforcementMode` is the global
  default (`Required`); `Compliance:Signals:Traces:EnforcementMode` and
  `…:Logs:EnforcementMode` override it per signal. Traces covers span, span
  event and link attributes. Metrics have no mode (see metric governance).
- `Required` drops + counts; `Disabled` registers nothing for that signal;
  `Warn` counts and logs would-be drops and exports unchanged. `Warn` is
  documented as a **weaker security posture**, not a debugging convenience.
  `raksawi.telemetry.attributes.would_drop` uses the same bounded key dimension
  as `AllowlistDropMetric` (capped distinct set, `{other}` overflow), never a
  value. Effective mode per signal published as gauge
  `raksawi.compliance.enforcement_mode` (dimension `telemetry.signal`, two
  values); a startup warning for every signal not `Required`.
- **Governance contract (closes "package omitted entirely").** Core knows only
  a neutral capability: a contribution may declare `ProvidesGovernance` and
  report, per signal, the mode it actually applied. Core never names
  Compliance. Kyc's onboarding and the Screening samples set
  `Governance:Required = true`.
  - It is a **startup validation over the final resolved configuration, run
    after the providers are configured** (not at registration): resolve
    configuration → configure providers → collect what each governance
    contribution reports it applied → validate.
  - Successful startup guarantees: (1) a governance-capable contribution was
    registered **and its configuration was actually invoked** for every
    enabled trace and log provider; (2) the applied mode for each enabled
    signal is `Required`; (3) baggage propagation is disabled; (4) no
    OpenTelemetry provider registration preceded `AddRaksawiObservability`
    (C1). A disabled signal (or `OTEL_SDK_DISABLED`) needs no coverage.
  - It does **not** guarantee: the collector's policy (CI and deployment, below),
    metric dimensions (analyzer + collector), or content of log bodies.
  - Referencing the package or registering an object without its configuration
    running does not satisfy it. Coverage is per enabled signal: a
    governance contribution that configures traces does not satisfy an
    enabled logs provider; startup fails naming the uncovered signal.
- **Three guarantees, named separately everywhere** (docs, ADR-0032,
  onboarding). Passing one is never described as proving another:

  | Guarantee | Proves | Checked by |
  |---|---|---|
  | Startup validation | required in-process governance was applied in this process | `Governance:Required` at startup |
  | Deployment validation | the collector runs the generated policy on every pipeline | `contract.sh` in CI and the deploy entrypoint |
  | End-to-end verification | prohibited canary data does not reach the backend | `e2e-instrumented.sh` |
- **Collector side.** The collector cannot know about the app, so this is
  enforced in CI and deployment: the deploy entrypoint always loads
  `compliance.generated.yaml`; `contract.sh` fails if any pipeline lacks the
  allowlist processor or the generated file is absent from the deploy command;
  e2e canary proves end-to-end absence.
- What `Compliance` *is*, by lifecycle: analyzer (build), runtime processors
  (startup option `EnforcementMode`), collector policy (CI generation),
  deployment contract (CI + `Governance:Required`). Turning one runtime option
  off never disables the analyzer or the generated policy.

### C4. Policy model and generator

Declarations → **resolved telemetry catalog** (internal to Compliance) →
validated policy → generated collector config → `otelcol validate` → fixture run.

- Each declaration resolves to: key, data class, permitted signals (spans,
  span events, logs, metric dimension yes/no — Class 2 is never a dimension),
  and an optional condition (a verification name, today only `url.redacted`).
- Generation **fails** on: conflicting declarations of one key; unknown data
  class; Class 3/4 declared (today silently ignored — becomes an error at
  generation, still ignored at run time); condition with no verifier; a rule
  the pinned collector version cannot express.
- Identical inputs → byte-identical output (ordinal sort, fixed formatting).
- Collector version pinned once (`COLLECTOR_VERSION`, today `0.140.1` repeated in
  `deploy/docker-compose.yaml`, `contract.sh`, `e2e-instrumented.sh`) and used by
  CI and deploy.
- Semantic tests run real OTLP fixtures (extending `.claude/scripts/e2e/payload-*.json`)
  through the generated config and assert per-key outcomes, alongside the
  byte comparison. Byte comparison proves reproducibility; fixtures prove meaning.
- **One authoritative definition, three adapters.** The rules and resolution
  logic live in one source file set — today's `AllowlistRules.cs` plus a new
  `TelemetryCatalog.cs` (conflict rules, permitted signals, conditions) — kept
  pure string logic with no reflection or Roslyn types, compiled by linked
  source into every consumer (the pattern `Raksawi.Observability.Analyzers.csproj`
  already uses for `AllowlistRules.cs`, and netstandard2.0-compatible for the
  analyzer). Only the *input* differs per consumer:

  | Consumer | Runs in | Reads declarations through |
  |---|---|---|
  | Runtime allowlist (Compliance) | the application | reflection over loaded/referenced assemblies, once at startup (`AttributeAllowlist`) |
  | Analyzer | the compiler | Roslyn assembly symbols (`DeclaredKeys`), no assembly loading |
  | Collector policy generator | build tooling | `System.Reflection.MetadataLoadContext` — metadata only, no code executed, no load-context conflicts |

  A test feeds the same set of declaring assemblies through all three adapters
  and asserts the resulting catalogs are equal. No new public package. This
  mechanism is frozen at the end of M2 together with the declaration
  attribute, markers and contribution interface.

### C5. CouchDB redaction guarantee

- The CouchDb package's HttpClient enrich hook runs after the instrumentation
  has set `url.full` at activity start and **overwrites** it with
  `CouchDbUrlPolicy.Redact` output (credentials removed, doc ids and lookup
  values replaced), and sets `url.query` consistently.
- The verification record is not a Boolean: the hook stores the redacted value
  it wrote (Activity custom property `raksawi.marker.url.redacted`, never a
  tag). Compliance admits `url.full`/`url.query` only if present **and equal**
  to the exported value — a later component that rewrites the URL raw makes
  the key drop, not leak.
- Any span without the record (other HTTP calls, other instrumentation
  libraries, unknown hosts) has the pair dropped by Compliance and by the
  collector. Fail-closed for every path the package does not own.
- Scope stated in docs: outgoing HttpClient request URLs to configured hosts.
  Not covered: URLs inside exception messages or log text (C3 pattern scan),
  response attributes (none carry the URL today), other libraries.
- Marker convention frozen in M2: `raksawi.marker.<subject>.<past-participle>`,
  declared only in core `TelemetryMarkers`, visible to the repo's signed
  packages via `InternalsVisibleTo`; new markers only by review of that file.
- Tests on **exported** telemetry: query parameters, embedded credentials,
  mixed-case host, `_changes` feed, a second enrich that rewrites `url.full`,
  a non-CouchDB host.

## Work breakdown — Phase 1 (.NET 10)

### M0 — Decision record (docs only)
- ADRs (next free number 0032):
  - **0032** Business-neutral core; compliance add-on; posture sentence; C3 surfaces and governance contract. Supersedes ADR-0001's split; reverses ADR-0003 for core-only services; **deviation** from Rev 3 I3.2.
  - **0033** NATS and CouchDB integration packages; C5 (resolves ticket 09).
  - **0034** Startup configuration and flags; C2; `EnforcementMode`.
  - **0035** Standard `OTEL_*` resolution; resource identity; `StrictEstateProfile` (amends 0006, 0008, 0010).
  - **0036** Declarations are core metadata; C4 catalog and generated collector policy; discovery roadmap (reflection once now, source-generated registry later for startup and NativeAOT) (amends 0017, 0018).
  - **0037** Grafana LGTM ahead of the I3.11 bake-off — **deviation** from Rev 3.
  - **0038** Pipeline composition: C1.
  - **0039** Tail sampling at the collector; services head-sample at 1.0 (amends ADR-0010).
  - **0040** Detection: alerting as code, email delivery, starting thresholds, SLOs and burn-rate alerting (implements ADR-0013's promotion of SLO alerting to Phase 1).
- `docs/adr/README.md` index and deviations list (seven → nine); `CLAUDE.md` count.
- `CONTEXT.md`: core, integration package, compliance add-on, vocabulary pack, contribution, stage, enforcement mode, governance contract, verification record, strict estate profile.
- Correct now-false statements found during review: `docs/allowlist.md` "fail opposite ways" and "covered by collector-side pattern scanning"; the "unsigned" remark in `AttributeAllowlist.cs` (assemblies are signed via `Directory.Build.props` + `raksawi.snk`).

### M1 — CouchDB leak fix on the current code (test-first, ships alone)
- Failing tests on exported telemetry: mixed-case configured host, `RedactCouchDbUrls = false`, enrich rewriting `url.full` afterwards.
- Fix with the C5 verification record in `RaksawiPipeline.TryRedactCouchDbUrl` and `AllowlistProcessor`.

### M2 — Core extraction (`src/Raksawi.Observability/`)
- **Start with the smallest vertical slice, verified before anything else
  moves:** core package, one-line registration, ASP.NET Core instrumentation,
  one custom activity, OTLP export, and a clean consumer restored from the
  local `.nupkg`. Then add C2 resolution, resource identity and the C1
  contribution infrastructure incrementally, each step re-running the slice.
- C1 builder (`RaksawiObservabilityBuilder.cs`, `IRaksawiObservabilityContribution.cs`), replacing `AddRaksawiIdentity`/`AddRaksawiExport` in `RaksawiPipeline.cs`.
- C2 resolution (`ObservabilityOptionsResolver.cs`); `RaksawiObservabilityOptions.cs` loses `CouchDbHosts`, `RedactCouchDbUrls`, `NatsActivitySourceName`; validation per C2.
- Keep `ServiceIdentity.cs` (resource per C2 table), `TraceContextMetric.cs`, `W3CTraceContextWarning.net10.cs`, `ConfigureOtlp`.
- Move in: `AllowedAttributeKeyAttribute`, `DataClass`, `TelemetryMarkers`; neutral `Correlation.cs` (from `KycTelemetry` correlation methods); governance capability check.
- Move out to Compliance/CouchDb as listed in M3/M4.
- **`samples/Neutral.Api` lands here**, plus `pack.sh` and a local-feed smoke test that restores core from the `.nupkg` into a scratch project.
- Verify against OpenTelemetry 1.17: deny-form metric views (C1/C3); the
  public descriptors used to detect a prior provider registration, and that a
  detected prior registration cannot yield an export path ahead of the filter
  (C1); exemplar default and filter override (C3).
- **Freeze at end of M2** (gate for M3/M4/M5): declaration attribute, marker
  convention, contribution interface and stages, catalog source and its three
  adapters (C4), environment variable contract (C2).

### M3 — Integration packages
- `Raksawi.Observability.Nats`: contribution (tracing `Sources`; metrics `Instruments`), declarations for the 13 keys now in `AllowlistRules.AllowedMessagingKeys`, `deploy/collector/nats.yaml` (current `prometheus/nats`, `transform/nats`, `metrics/nats`). Registers `NATS.Net` exactly once; no assumption that the source exists if NATS.Net is not loaded.
- `Raksawi.Observability.CouchDb`: contribution (tracing `Instrumentation` filter for `_changes`, `Enrich` redaction per C5); `CouchDbUrlPolicy.cs` moved as is; `couchdb.database`/`couchdb.conflict` declarations moved from Kyc `AssemblyInfo.cs`.

### M4 — Compliance add-on
- `AllowlistRules.cs` keeps families, carve-outs, resource rules; loses `AllowedMessagingKeys` and the static `NeverAMetricDimension`; `isCouchDbSpan` replaced by the C5 verification check.
- Catalog (C4) built once at startup and shared by span and log processors (today `FromLoadedAssemblies()` runs twice).
- New coverage per C3: span event and link attributes; `TagKeys`-restricting views rejected under governance (exemplar safety); `OTEL_PROPAGATORS`/`OTEL_METRICS_EXEMPLAR_FILTER` validation under governance; baggage disabled on both propagators; global + per-signal `EnforcementMode`; `ProvidesGovernance` with applied-mode reporting.
- Analyzer packed in the Compliance nupkg; reads the catalog's Class 2 keys; diagnostics reviewed for actionable messages.

### M5 — Collector policy generation (starts after M2, parallel with M3/M4)
- `tools/Raksawi.Observability.CollectorPolicy/` per C4; first target: reproduce today's hand-written `transform/allowlist` byte-for-byte, then switch to generated.
- Collector: `base.yaml` (neutral: OTLP 4318, memory_limiter, batch, resource, exporters, ADR-0031 self-scrape) + `nats.yaml` + `compliance.generated.yaml` (adds `spanevent` rules and the ADR-0004 pattern scan of log bodies and `exception.message`).
- Metric Class 2 removal follows the C3 merge table; exemplar attributes removed or exemplars dropped; gauge/exponential-histogram violations dropped and counted. Proven with fixtures asserting values, point counts, start times, temporality and buckets.
- `CollectorAllowlistContractTests.cs` → structural tests + byte comparison + fixture semantics. `contract.sh` checks the deploy command includes the generated file.

### M6 — Grafana backend (parallel any time after M0)
- `deploy/docker-compose.yaml`: `grafana/otel-lgtm` for the demo; exporter/endpoint configuration validated against that image, not assumed. Production topology (separate Tempo/Loki/Mimir + Grafana) documented in `docs/onboarding/infra.md`.
- Collector exporter `otlp/signoz` → `otlphttp`; persistent queue kept.
- Dashboards: `docs/diagnostic-queries.md` panels transcribed to provisioned Grafana JSON (`deploy/grafana/dashboards/`), plus "services not at `Required`".
- e2e scripts: rename the `signoz-ingester-1` sink alias.
- Out of scope: multiple demo backends, dashboard-generation tooling.

### M7 — KYC migration and samples
- Kyc depends on Compliance; correlation methods become `[Obsolete]` forwarders for one release.
- Screening samples: `.AddNats().AddCouchDb(...).AddCompliance()`, `StrictEstateProfile` and `Governance:Required` true, settings in `appsettings.json`.
- Test-only `order.id` vocabulary pack: allowed on spans/logs, never a dimension, present in generated policy, no core or Compliance edit.
- Docs rewritten (README, onboarding, `samples/README.md`, `docs/allowlist.md`, `deploy/README.md`, `CLAUDE.md`), each opening with the posture sentence.

### M8 — Packaging and release validation
- `Directory.Packages.props` (central package management); OpenTelemetry family pinned together (today `OpenTelemetryVersion` 1.17.0); supported version range documented.
- Common metadata (license, repo URL, SourceLink, deterministic, `.snupkg`, package README); version `2.0.0`; core and Compliance `PackageDescription` carry the posture sentence.
- Per-package release test: pack, inspect contents (analyzer under `analyzers/dotnet/cs` in Compliance), restore into a clean project from a local feed, compile the public API, run. Two clean consumers: core-only, and Compliance-only (verifies analyzer delivery and diagnostics with no extra references).
- The local-feed smoke from M2 runs at every milestone, not only here.
- Obsolete `AddRaksawiObservability(Action<RaksawiObservabilityOptions>)` overload kept for one release.

### M9 — Detect and diagnose (on today's code, right after M6-min)
Independent of the package refactor: it lives in the collector and Grafana.
- **Alerting as code** in `deploy/grafana/alerting/` (provisioned rules, contact point, notification policy), delivered by email:
  - SMTP settings from environment variables (`GF_SMTP_*`), never in the repo; recipient list per environment.
  - Demo: a local mail catcher container (Mailpit) in `deploy/docker-compose.yaml`, so every alert can be seen and asserted on in e2e without a real mail server.
  - Every alert email names the service, the panel and the runbook link; no Class 2 values in alert labels or text (labels are metric dimensions, so this already holds — asserted in tests).
- **Starting thresholds** (until M10's SLO alerts replace the first three):

  | Alert | Condition | For | Why this value |
  |---|---|---|---|
  | Work abandoned | `screening.applications.abandoned` increase > 0 | 5 min | every abandonment is a customer whose application silently stopped; there is no acceptable rate |
  | Error rate | 5xx ÷ requests > 5% per `service.name` + `http.route`, with ≥ 20 requests in the window | 10 min | the volume floor stops a single failed request on a quiet route from emailing anyone |
  | Latency | p95 `http.server.request.duration` > 2 s per route, ≥ 20 requests | 10 min | `POST /applications` does two CouchDB calls and one NATS publish; 2 s is several times a healthy request |
  | Screening slow | p95 `screening.duration` > 10 s | 15 min | covers the "slow" demo fault (3 s per lookup) only when it is widespread |
  | Dead man's switch: services | a service in the register stops reporting `target_info` | 5 min | four separate alerts, one per state of panel 4.11, so the email says *which* part is dead |
  | Dead man's switch: collector receiving | `otelcol_receiver_accepted_spans` rate = 0 | 5 min | |
  | Dead man's switch: collector exporting | sent rate = 0 or failed rate > 0 | 5 min | also closes finding 07 (export failure rate reads zero during an outage) |
  | Dead man's switch: store | Grafana data source health check fails | 2 min | evaluated by Grafana itself, so it fires even when the pipeline is down |
  | Collector queue | queue size > 80% of capacity | 10 min | early warning before the persistent queue drops data |
  | Not at `Required` | `raksawi.compliance.enforcement_mode` ≠ Required (after M4) | 1 min | governance regression |

  The dead man's switch evaluated inside the same Grafana it watches cannot report Grafana itself being down; the store alert plus an external heartbeat (a daily "alerting is alive" email that stops arriving) covers that, documented as the residual.
- **Runbooks**: `docs/runbooks/<alert>.md`, one page each — what it means, first query to run (from `docs/diagnostic-queries.md`), likely causes, who owns it. Linked from the alert. (Rev 3 D4.5 for the alerts that exist.)
- **Tail sampling** in the collector (`tail_sampling` processor) before the exporter: keep all traces with an error status, all slower than 2 s, all with `screening.abandoned = true`, plus 10% of the rest. Fills panel 2.6. Memory sized from measured span rate; decision wait set above the longest healthy trace. With more than one collector instance, a trace-id-aware `loadbalancing` exporter tier is required in front — documented for production, not built for the single-collector demo.
- **Grafana correlations** provisioned with the data sources: Tempo → Loki (logs for this trace by `trace_id`), Loki → Tempo (derived field on `trace_id`), Mimir exemplars → Tempo. Tested by opening a trace from a latency exemplar in the e2e run.
- **Small panels**: process instrumentation (1.4), collector queue age (4.4) or the nearest available proxy, recorded if none exists.
- **Outcome baseline**: time to detect and time to diagnose for the three demo faults (slow, fail, abandon), measured before M9 and after, written to `docs/phase2/mttr-demo.md`. Repeated after M10.

### M10 — Async pipeline, SLOs and coverage (end of phase 1)
- Sample moved to **JetStream** with a durable consumer and a DLQ subject: fills panels 3.1 (oldest unprocessed message age), 3.2 (consumer lag), 3.3 and 3.6 (DLQ). Redelivery replaces the in-process retry loop's "work is gone" behaviour; `causation.id` keeps redelivery and retry distinguishable (ADR-0007). NATS package declarations extended for the JetStream keys the sink actually shows (ADR-0018 empirical rule).
- **SLOs** for screening-api and screening-worker as the worked example, defined in `docs/slos.md`:
  - availability: 99.5% of `POST /applications` and `GET /applications/{id}` non-5xx over 28 days;
  - latency: 95% of those requests under 1 s over 28 days;
  - completion: 99% of submitted applications screened within 5 minutes over 28 days (from JetStream consumer metrics and `screening.applications.screened`).
- **Burn-rate alerts** (multi-window): email when 2% of the 28-day budget burns in 1 hour (checked over 1 h and 5 min), and when 5% burns in 6 hours (6 h and 30 min); a weekly summary email for slow burn. These replace the M9 fixed error-rate and latency alerts for the two services; the fixed ones remain the default for any service without an SLO.
- **Service register** (ADR-0021): `deploy/service-register.yaml`, PR-gated; feeds the coverage panel (4.1) and the "service stopped reporting" alert.
- Remaining stack panels where a source exists: freshness probe (4.2), backup restore age (4.8) documented as an infrastructure task with its alert.

### Sequencing (superseded by "Production deliverables" — kept for the reasoning)
M0 → M1 → M6-min → M9 → M2 → (M3 ∥ M4 ∥ M5) → M7 → M10 → M8, with the rest of M6 parallel after M6-min.
- M1 ships alone.
- M6-min is the Grafana backend and collector exporter only; dashboards and the rest of M6 follow in parallel.
- M9 runs on today's library, so detection exists before the refactor starts; M2–M8 must keep every M9 alert green (the e2e run asserts the alert emails).
- M10 needs the NATS package (M3) for the JetStream declarations.
- M3/M4 start only after M2 has frozen the declaration attribute, markers and contribution interface, so they cannot diverge.
- M5's semantic tests come before it replaces the hand-written policy.
- `Neutral.Api` and the local-feed smoke exist from M2 onward.

### Deferred (not on the phase 1 critical path)
.NET Framework 4.8 · multiple backends · plugin discovery · runtime reconfiguration · other policy formats · a general policy language · source-generated declaration registry · new library metrics beyond the drop/would-drop counters and the enforcement gauge.

## Phase 2 — .NET Framework 4.8
- New ADR reversing ADR-0029; restore `net48;net10.0` for core, integrations, Compliance, Kyc.
- 4.8 entry point `RaksawiObservability.Start(...)` returning `IDisposable`, built on the same C1 contributions over `Sdk.CreateTracerProviderBuilder()`; System.Web instrumentation contribution; `HttpWebRequest` hooks for CouchDb; logs per ADR-0028; tickets 11 and 12.

## Risks
- **Core-only services emit unfiltered** (largest risk): accepted (Q2); mitigated by the posture sentence everywhere, `Governance:Required` for regulated services, mandatory generated collector policy, unchanged ADR-0009 posture.
- **Foreign OTel registration bypassing the filter**: documented unsupported; RKS003.
- **Collector free-text and span-event gaps exist today**: closed in M4/M5, recorded in ADR-0032 rather than fixed silently.
- **Breaking change** for Azure Artifacts consumers: 2.0.0, obsolete forwarders, migration section.
- **Performance**: re-run the ADR-0014 baseline (`docs/phase0/performance-baseline.md`) for processors (now incl. events), catalog lookup and redaction; budget stated in ADR-0034.
- **Capacity**: phase 1 is now about 23.5 engineer-weeks (my estimate, ±40%). At the recorded ~0.25 FTE that is close to two years of calendar time; the plan assumes either more capacity or accepting that pace. M9 first means detection value lands within the first two months regardless.
- **Alert noise**: starting thresholds are guesses until real traffic exists (ADR-0027: no baseline data source). Review after two weeks; an alert that fires without action is changed or removed, not muted.
- **Tail sampling cost**: collector memory grows with decision wait × span rate; measured in M9 and sized in `docs/onboarding/infra.md`.
- **AGPLv3** Grafana stack: operated, not linked (ADR-0037). Rev 3 deviations +2, both stated.

## Verification
- Every milestone: `.claude/scripts/check.sh`, `.claude/scripts/test-fast.sh`, local-feed smoke.
- Unit/integration matrix (per package test project), all asserting on exported telemetry via the in-memory exporter:
  - Core config: parameterless setup; precedence across defaults/`OTEL_*`/section/code; empty vs false vs invalid; `OTEL_SDK_DISABLED` beats `Traces:Enabled = true` in code; derived instance id warning in a container; traces off with metrics on; resource identity table; sampler mapping incl. parent-based.
  - Pipeline: reverse-stage registration executes in stage order; same-stage registration order preserved; late `Process` registration; multiple filters ordered; prior foreign provider registration fails startup under `Governance:Required`; duplicate core/integration registration (no-op vs throw); disabled instrumentation not registered; flush on host stop.
  - Compliance: span/event/link/log attribute drops; resource handled by collector; exception events; Class 2 metric dimension removed at collector; baggage not propagated; global/per-signal mode resolution; exemplar leak test (Class 2 on a measurement with an active sampled trace: absent from points and exemplars); governance fails when an enabled signal has no covering contribution; `Warn` and `Disabled` behaviour, gauge and warning; `would_drop` cardinality cap; `Governance:Required` fails startup without a governance contribution, with one registered but not invoked, and with any enabled signal not `Required`; passes with `OTEL_SDK_DISABLED`; inbound/outbound trace context propagated and baggage not, on ASP.NET Core, HttpClient and NATS.
  - Integrations: NATS on/off/duplicate, metrics with traces off; CouchDB per C5 list.
  - Generator: every C4 failure case; byte-identical regeneration; fixture semantics; every row of the C3 merge table with value assertions; three-adapter catalog equality.
- `.claude/scripts/contract.sh`: generated file matches; deploy command includes it; every pipeline filtered; `otelcol validate` at the pinned version.
- `.claude/scripts/e2e.sh`, `e2e-instrumented.sh` (docker): PII canary absent from every signal including span events, log bodies and metric exemplars; CouchDB `url.full` redacted; `Neutral.Api` exports with no NATS source, no compliance meter.
- M9/M10: e2e triggers each demo fault and asserts the matching email arrives in the mail catcher within its `for` window, and that stopping the collector, the exporter and the store each produces its own dead-man's-switch email; tail sampling keeps 100% of error and abandoned traces under a 10% base rate; an exemplar on the latency panel opens its trace; SLO burn-rate rules evaluate correctly on a replayed fixture; the MTTR baseline is recorded before and after.
- Release gate: `docs/compliance-surfaces.md` has every row *Verified* or carrying an accepted limitation; ADR-0014 performance baseline re-run and within the ADR-0034 budget.
- Manual: `docker compose up` in `deploy/`, Grafana dashboards render the Screening flow (submit → screen → retry → abandon).

## Review log

**Revision 7** — cut into production deliverables with the facts above: nothing yet in production, one release line, EKS with S3 and SES, the screening worker on .NET 10 using core NATS, no staging (so D4 creates one). M2, M4 and M5 were each larger than two weeks and were split (D6a/b, D8a/b, D9a/b). ADRs move into the deliverables that need them instead of one up-front milestone.

**Review 1** — adopted: `EnforcementMode` (default `Required` everywhere, not `Warn` in Development, so drops are seen locally); `Process` stage; marker convention without speculative markers; source-generator roadmap in ADR-0036; posture sentence; M5 earlier.

**Review 2** — adopted: C1–C5 as pre-M2 contracts; signal-aware contributions; duplicate-registration policy; explicit config resolution with exported-value tests; compliance surface table and four-layer framing; governance contract; policy catalog with failing generation; pinned collector version and fixture tests; verified (not Boolean) redaction record; Neutral.Api and package smoke from M2; central package management; resource identity, shutdown, diagnostics, sampling and performance items; defer list.
Clarified rather than adopted as written:
- The Production-name boot guard it critiques was already replaced by `EnforcementMode` in revision 2; the "package omitted" gap is closed by `Governance:Required` instead.
- "The collector rejects a missing policy" is not something a collector can do; it is enforced in CI/deploy (`contract.sh`) and proven by the e2e canary.
- HttpClient instrumentation sets `url.full` at activity start and the enrich hook overwrites it; the residual risk is later rewrites and other libraries, which C5's value-equality check and fail-closed default address.
- Metrics get no in-process deny filter unless the SDK offers one (C1).
**Review 3** — approved with five conditions, all settled: `OTEL_SDK_DISABLED` as a non-overridable kill switch (C2); explicit stage-vs-registration ordering tests and a runtime check for a prior foreign provider registration (C1); metric governance acceptance criteria incl. collector re-aggregation and stated SDK cardinality limit (C3); global + per-signal `EnforcementMode`, no metric mode, `Warn` as weaker posture (C3); `Governance:Required` as post-configuration validation with an exact guarantee list (C3). Also adopted: surface table as release gate with test/limitation/status; pattern scanning as compensating control; `service.instance.id` deployment assumption; process-wide baggage trade-off on both .NET propagators; M2 vertical slice first.

**Review 4** — approved for implementation. Adopted: exemplar leakage as a P0 condition on metric governance (filter forced off, collector removal, regression test); per-type re-aggregation semantics with value-level fixtures; catalog consumption as one linked source with three input adapters, frozen at end of M2; the supported `OTEL_*` contract table incl. explicitly unsupported variables (and two the review did not name — `OTEL_METRICS_EXEMPLAR_FILTER`, `OTEL_PROPAGATORS` — which could undo governance); the three guarantees named separately; a per-signal coverage failure test. Decided beyond the review: gauges and exponential histograms carrying a Class 2 key are dropped and counted rather than merged, because no merge is correct.

**Observability-value review (revision 6)** — the plan improved reach and safety but not detection: no alerting, SLOs, tail sampling, cross-signal links or runbooks, and 15 of 30 panels without data, none filled by the plan. Decided: M9 (detect and diagnose) and M10 (async, SLOs, coverage) both in phase 1; M9 runs right after a minimal Grafana backend on today's code; email delivery; SLO-based thresholds as the end state with fixed starting thresholds; exemplars kept with attributes stripped.

Found while checking review 2 against the code: span event and link attributes are filtered nowhere; ADR-0004's collector pattern scan does not exist in `config.yaml`; the baggage rule is a comment only. All three are scheduled (C3, M4, M5).
