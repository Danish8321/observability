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
the deviation and its reasoning. Five such deviations exist and are listed in the
[ADR index](./docs/adr/README.md#deviations-from-rev-3). Nothing deviates
silently.

## What this repository produces

| Artifact | Consumed by | Target |
|---|---|---|
| `Raksawi.Observability` | any estate service | `net48;net10.0` |
| `Raksawi.Observability.Kyc` | KYC services | `net48;net10.0` |
| `Raksawi.Observability.Analyzers` | build only, `PrivateAssets="all"` | `netstandard2.0` (Roslyn) |
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
| `contract.sh` | Collector policy and the declared allowlist express the same rules | working — but **fails without a validator**: the text comparison passes, and `otelcol validate` is required before the OTTL can be called correct |
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
`net48;net10.0` except the sample (`net10.0` only). Twenty-four ADRs, a
glossary, and five Phase 0 worksheets whose data has **not** been collected —
that data, not more code, is the current bottleneck.

The library-before-export enforcement point became real on 2026-08-24:
`AttributeAllowlist` + `AllowlistProcessor` drop any span attribute whose key
is not allowlisted, wired as the last processor before the exporter on both
runtimes (ADR-0003/0017/0018). Before that date this README claimed it existed
when only the narrow `CouchDbUrlPolicy` redaction did.

The build-time enforcement point followed on the same day:
`TelemetryGovernanceAnalyzer` raises RKS001 (undeclared attribute key),
RKS002 (Class 2 as a metric dimension, an error) and RKS003 (exporter
configured by hand), reading the same `AllowedAttributeKey` declarations the
runtime reads and compiling the same `AllowlistRules` table, so build-time and
run-time enforcement cannot disagree (ADR-0002/0017). It is deliberately
literal-only: a key computed at run time is left to the runtime allowlist,
because a false positive on a governance rule teaches people to suppress
governance rules.

The collector-side point (ADR-0009) followed: `transform/allowlist` in
`deploy/collector/config.yaml` denies the carve-outs, resolves the
`url.full`/`url.query` conditional against the CouchDB host list, then keeps by
family — so anything unnamed is gone by default — and refuses the Class 2 keys
as metric dimensions on the metrics pipeline. `error_mode: propagate` makes it
fail closed. This is the *only* enforcement point that reaches
agent-instrumented 4.8 services. `contract.sh` fails if it and
`AllowlistRules.cs` drift.

All three enforcement points now exist. Two caveats, both open: the OTTL has
**not** yet been through `otelcol validate` (no validator available on the
machine it was written on, and `contract.sh` fails rather than skipping), and
the collector filters span and datapoint attributes but **not resource
attributes**, which an agent-instrumented service supplies itself via
`OTEL_RESOURCE_ATTRIBUTES`.
🔒 Neither package is strong-named yet, so ADR-0017's provenance check on
allowlist declarations passes vacuously — see [`docs/allowlist.md`](./docs/allowlist.md). Ten accepted ADRs are deliberately unimplemented until
after the demo ([ADR-0022](./docs/adr/0022-demo-first-resequencing.md)).
CouchDB URL redaction
([ADR-0023](./docs/adr/0023-couchdb-changes-the-database-surface.md)) fails
open by design; both the redaction and the fail-open path were verified
against real spans on 2026-08-11
(`.scratch/demo-readiness/issues/01-verify-couchdb-redaction-real-span.md`).

What is unresolved is tracked in
[`docs/open-questions.md`](./docs/open-questions.md) rather than in anyone's
head. Blocking today: the two names in
[ADR-0019](./docs/adr/0019-delegated-data-protection-ownership.md) and the
[regulatory request](./docs/regulatory-request.md), which is drafted but
unsent. SSO is answered — not mandatory (Q3, 2026-08-10) — and does not
disqualify either candidate store at I3.11.

D0.3's estate inventory closed 2026-08-24 by working position, not by the
five-source sweep Rev 3 specified — none of those five sources are reachable
at all ([ADR-0024](./docs/adr/0024-estate-inventory-by-working-position-not-sweep.md)).
The performance baseline (D0.2, [Q5](./docs/open-questions.md)) is next in
[Phase 0](./docs/phase0/) and remains the only measurement in the programme
that expires — but its Run 0 needs the same kind of estate access (IIS logs,
Windows performance counters, NATS/CouchDB monitoring) that D0.3 just found
unobtainable. Confirm access before starting it, rather than assuming it will
go the same way.
