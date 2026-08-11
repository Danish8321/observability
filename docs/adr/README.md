# Architecture decision records

Every decision made for this platform, in the order it was made. Rev 3 of the
observability implementation plan owns policy and sequencing; where an ADR
deviates from it, the deviation is stated in that ADR rather than applied
silently.

| # | Decision | Status |
|---|---|---|
| [0001](./0001-two-layer-observability-package.md) | Two-layer package: mechanism and policy | Accepted |
| [0002](./0002-closed-allowlist-provenance.md) | Allowlist sources are a closed set | Accepted; mechanism superseded by 0017 |
| [0003](./0003-runtime-allowlist-at-source.md) | Source redaction is a runtime allowlist, not a deny-list | Accepted |
| [0004](./0004-free-text-telemetry-and-exceptions.md) | Free text: interpolation banned at build, scanned at collector | Accepted |
| [0005](./0005-enforcing-the-framework-wiring.md) | The .NET Framework wiring is enforced, not documented | Accepted; rules built after Phase 2 observation |
| [0006](./0006-service-identity-convention.md) | Service identity: bare name plus namespace | Accepted |
| [0007](./0007-correlation-lifetime.md) | Correlation minted at workflow start, seeded by the browser | Accepted |
| [0008](./0008-service-instance-identity.md) | Service instance identity: supplied if known, derived if not | Accepted |
| [0009](./0009-governing-agent-instrumented-services.md) | Agent services governed at the collector, fail-closed | Accepted |
| [0010](./0010-sampling-defaults.md) | Absent sampler configuration is a production boot failure | Accepted |
| [0011](./0011-estate-vocabulary-versus-domain-vocabulary.md) | Tenancy is estate vocabulary; screening is domain vocabulary | Accepted; `tenant.id` withheld until D0.3 |
| [0012](./0012-net10-first-sequencing.md) | .NET 10 first, multi-targeted from the first commit | Accepted |
| [0013](./0013-abort-criterion-becomes-a-reordering.md) | The Phase 0 abort criterion reorders rather than stops | Accepted — deviates from Rev 3 |
| [0014](./0014-performance-baseline-method.md) | Baselines taken now, both runtimes, repeatable script | Accepted |
| [0015](./0015-regulatory-assumptions-pending-written-answer.md) | Proceed on assumed-strictest regulatory constraints | Accepted — provisional |
| [0016](./0016-diagnostic-queries-are-the-durable-asset.md) | Diagnostic queries specified store-neutrally | Accepted |
| [0017](./0017-allowlist-declared-as-assembly-attributes.md) | Allowlist declared as assembly attributes, not a manifest | Accepted — supersedes part of 0002 |
| [0018](./0018-allowlist-composition.md) | Allowlist is families with carve-outs, validated empirically | Accepted |
| [0019](./0019-delegated-data-protection-ownership.md) | Data protection ownership delegated technically, countersigned | Accepted — pending two names |
| [0020](./0020-telemetry-access-tiers.md) | Two access tiers: operational broad, audit restricted | Accepted |
| [0021](./0021-service-register-is-the-coverage-denominator.md) | The service register lives here and is reconciled against reality | Accepted |
| [0022](./0022-demo-first-resequencing.md) | A demo precedes Phase 0; compliance deferred, not cancelled | Accepted — resequences Rev 3 |
| [0023](./0023-couchdb-changes-the-database-surface.md) | CouchDB moves database risk from statement text to the URL | Accepted — corrects 0003, 0004, 0018 |

## Deviations from Rev 3

Rev 3 states that where it and a companion document disagree, Rev 3 wins. These
are the places this repository knowingly differs, each argued in its own ADR:

- **0007** — five correlation identifiers where **D2.3** specifies four. A page
  load and a business workflow are different lifetimes and were being served by
  one identifier.
- **0013** — **D0.1**'s abort criterion promotes SLO alerting to Phase 1 instead
  of stopping the project, because the alternative plan needs the same collector
  and the same `service.name` convention.
- **0011** — `tenant.id` is treated as estate vocabulary rather than sitting with
  the other Class 2 identifiers **D2.1** groups together.
- **0004**, **0009** — the collector acts as a primary control rather than the
  net **I3.2** describes, in the two cases where no source-side control exists:
  free text, and processes containing none of our code.

## Grouping

**Package shape** — 0001, 0011, 0012, 0017
**Governance and compliance** — 0002, 0003, 0004, 0009, 0015, 0018, 0020
**Telemetry schema** — 0006, 0007, 0008
**Runtime behaviour** — 0005, 0010
**Programme** — 0013, 0014, 0016, 0019, 0021, 0022
**Estate facts** — 0023

## Deferred by ADR-0022 until after the demo

Accepted and unimplemented. Deferred is not cancelled, and the list exists so
that "we never did that" stays distinguishable from "we decided not to yet":
0002, 0003, 0014, 0015, 0016, 0017, 0018, 0019, 0020, 0021.

The boundary that makes deferral safe: **the demo does not touch production KYC
traffic.**
