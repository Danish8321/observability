# Raksawi observability platform

Governance, shared libraries, and platform configuration for observability across
the Raksawi estate — .NET 10 and .NET Framework 4.8, NATS and HTTP, SQL.

The goal is **reduced MTTR**, not installed OpenTelemetry. Coverage is a means;
it is never the measure.

## Authority

The observability implementation plan (Rev 3) owns policy and sequencing. This
repository owns execution, and records its decisions in
[`docs/adr/`](./docs/adr/).

Where Rev 3 and an ADR disagree, **Rev 3 wins** unless the ADR explicitly states
the deviation and its reasoning. Four such deviations exist and are listed in the
[ADR index](./docs/adr/README.md#deviations-from-rev-3). Nothing deviates
silently.

## What this repository produces

| Artifact | Consumed by | Target |
|---|---|---|
| `Raksawi.Observability` | any estate service | `net48;net10.0` |
| `Raksawi.Observability.Kyc` | KYC services | `net48;net10.0` |
| `Raksawi.Observability.Analyzers` (not yet built) | build only, `PrivateAssets="all"` | Roslyn |
| Collector configuration | platform deployment | PR-gated, `otelcol validate` in CI |
| Diagnostic query specifications | dashboards and runbooks | store-neutral |
| Governance | humans and audit | this file, `CONTEXT.md`, `docs/adr/` |

Service code lives in its own repositories and consumes the packages from Azure
Artifacts. This repository contains no service code.

## What integration looks like

**.NET 10** — one package reference, one line:

```csharp
builder.AddRaksawiObservability();
```

Set `OTEL_SERVICE_NAME` and `DEPLOYMENT_ENVIRONMENT`. Traces, metrics, logs,
resource attributes, redaction, sampler defaults, and exporter safety all follow.
The service names no database, no endpoint, and no sampling rate.

**.NET Framework 4.8, SDK path** — a package reference plus three touchpoints the
library cannot reach from inside: W3C forcing as the first statements of
`Application_Start`, the returned `IDisposable` held for the application lifetime
and disposed in `Application_End`, and the telemetry module registered in
`Web.config`.

**.NET Framework 4.8, agent path** — no package at all. The auto-instrumentation
MSI on the host, `Register-OpenTelemetryForIIS`, and environment variables per
application pool. Governance for these services is applied at the collector, not
in-process — see [ADR-0009](./docs/adr/0009-governing-agent-instrumented-services.md).

## The shape of the design

**Two layers.** Mechanism knows how telemetry is produced and shipped. Policy
knows what a business domain may say. A non-KYC service takes mechanism alone.
[ADR-0001](./docs/adr/0001-two-layer-observability-package.md).

**Three enforcement points, default deny at each.** The analyzer at build, the
library before export, the collector before storage. A process containing none of
our code has only the third, and gets it fail-closed.
[ADR-0003](./docs/adr/0003-runtime-allowlist-at-source.md),
[ADR-0009](./docs/adr/0009-governing-agent-instrumented-services.md).

**One source for what may be said.** The allowlist is declared as assembly
attributes in policy packs and read by both the analyzer and the runtime. There
is no manifest, so there is nothing to drift.
[ADR-0017](./docs/adr/0017-allowlist-declared-as-assembly-attributes.md),
[ADR-0018](./docs/adr/0018-allowlist-composition.md).

**Telemetry never fails a business request.** Batch export only, bounded
timeouts, no synchronous flush on the request path. Where a control would cost
unbounded work on that path, it moves to the collector instead.

## Sequencing

Governance is written first; code follows evidence.

1. **Phase 0** — incident decomposition, performance baselines, estate
   inventory, route and subject audits. No code. The baselines
   ([ADR-0014](./docs/adr/0014-performance-baseline-method.md)) expire and are
   taken first.
2. **Gate 0**, then **Phase 1** — hand-wired local proof on .NET 10. The
   hand-wiring is the specification for `AddRaksawiObservability()`; the library
   is not the way to discover what the library should do.
3. **Gate 1**, recorded as **partial** — the 4.8 rows stay open under
   [ADR-0012](./docs/adr/0012-net10-first-sequencing.md), with a named owner.
   Code starts here.
4. **Phase 2** — the packages, the analyzer, 4.8 runtime validation, the shared
   environment, the bake-off.
5. **Phase 3 onward** — hardening, PII audit, failure matrix, production.

Both packages multi-target `net48;net10.0` from the first commit even though
.NET 10 is worked first. The `net48` build failing is the cheapest available test
of a constraint that is expensive to retrofit.

## Verification

Nothing is described as working without evidence from a named script. All five
exist; two of them fail on purpose.

| Script | Proves | Status |
|---|---|---|
| `check.sh` | Both targets build, formatting holds | working |
| `test-fast.sh` | Unit tests, no collector/store/network | working |
| `test-full.sh` | The above, plus `otelcol validate` on collector configuration | working |
| `contract.sh` | Collector policy and the declared allowlist express the same rules | **honest-failing stub** — no code-side allowlist declaration (ADR-0017) and no collector allowlist processor (ADR-0003) exist yet, so there is nothing to diff |
| `e2e.sh` | Assertions against *received* telemetry, not configuration | **honest-failing stub** — no live collector/store stack with allowlist enforcement to assert against |

`contract.sh` and `e2e.sh` exit 1 with an explanation rather than passing
vacuously or being omitted. They turn green only when their prerequisites
land, per the sequencing below.

`e2e.sh` exists because Rev 3 **Gate 3** requires redaction verified by
inspecting stored data. A test that reads configuration would verify intent, not
outcome.

CI is Azure Pipelines. Every stage calls one of these scripts; the scripts are
the contract and the pipeline is only a caller.

## Where things are

```
CONTEXT.md                    glossary — the shared vocabulary, nothing else
README.md                     this file — scope, shape, sequencing
docs/adr/                     every decision, in the order it was made
docs/adr/README.md            index, groupings, and deviations from Rev 3
docs/open-questions.md        live register of what is unresolved
docs/allowlist.md             families, carve-outs, Class 2 keys — reviewed intent
docs/diagnostic-queries.md    store-neutral panel and runbook specifications
docs/regulatory-request.md    the I0.1 request, drafted, not yet sent
docs/phase0/                  worksheets — method decided, data not collected
docs/phase2/                  4.8 failure-mode validation procedure
docs/phase3/                  store bake-off criteria, failure matrix
```

## Current state

Code exists and builds: `Raksawi.Observability`, `Raksawi.Observability.Kyc`,
the `Screening` sample (API, domain, worker), and unit tests, all on
`net48;net10.0` except the sample (`net10.0` only). Twenty-three ADRs, a
glossary, and five Phase 0 worksheets whose data has **not** been collected —
that data, not more code, is the current bottleneck.

Not yet built: `Raksawi.Observability.Analyzers` (build-time enforcement point,
ADR-0003) and the collector-side allowlist processor (also ADR-0003) — so of
the three enforcement points the design calls for, only one (library before
export) is real today. Ten accepted ADRs are deliberately unimplemented until
after the demo ([ADR-0022](./docs/adr/0022-demo-first-resequencing.md)).
CouchDB URL redaction
([ADR-0023](./docs/adr/0023-couchdb-changes-the-database-surface.md)) fails
open by design and has not yet been verified against a real span.

What is unresolved is tracked in
[`docs/open-questions.md`](./docs/open-questions.md) rather than in anyone's
head. Blocking today: the two names in
[ADR-0019](./docs/adr/0019-delegated-data-protection-ownership.md), the
[regulatory request](./docs/regulatory-request.md) which is drafted but unsent,
and whether SSO is mandatory — which must be answered before the store decision
at I3.11, not after.

Next action is [Phase 0](./docs/phase0/), starting with the performance
baseline, because it is the only measurement in the programme that expires.
