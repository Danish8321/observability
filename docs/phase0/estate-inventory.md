# D0.3 — Estate inventory

**Status:** not started
**Satisfies:** Rev 3 **D0.3**, **D0.4**, and the D2.9 path determination

---

## Why this is more than a service list

Rev 3 asks for four fields. Decisions taken since have made this inventory the
input to several of them, and two are load-bearing:

| Consumer | What it needs | What happens if the answer is unexpected |
|---|---|---|
| [ADR-0011](../adr/0011-estate-vocabulary-versus-domain-vocabulary.md) | Is the estate mixed, or uniformly KYC? | `tenant.id` is withheld from both packages until this answers. A uniformly KYC estate means promoting it was speculative generality, and ADR-0011 is revised before any code exists |
| [ADR-0012](../adr/0012-net10-first-sequencing.md) | Will *any* 4.8 service be recompiled? | If none will, the `net48` target has no purpose. Multi-targeting from commit one becomes dead weight, and ADR-0001 and ADR-0012 are both revisited |
| [ADR-0009](../adr/0009-governing-agent-instrumented-services.md) | Which services take the agent path | Agent services are governed at the collector, fail-closed, and must be enumerated in this repository |
| Rev 3 **D0.4** | `NATS.Net` version per service | Below 3.0.1 means no built-in tracing or metrics, and the plan's NATS assumptions do not hold |
| Rev 3 **D0.4b** | Which runtime owns the AJAX endpoints | Decides who owns the route-identifier fix |

## Worksheet — one row per service

| Field | Notes |
|---|---|
| Service name | Proposed `service.name`, per [ADR-0006](../adr/0006-service-identity-convention.md) — bare kebab-case |
| Namespace | Proposed `service.namespace` — the domain |
| Domain | KYC, or something else. **This is the ADR-0011 answer** |
| Runtime | .NET 10 or .NET Framework 4.8 |
| Hosting | Kestrel, IIS, Windows Service, container |
| Transports | HTTP in, HTTP out, NATS, CouchDB *(itself HTTP — see [ADR-0023](../adr/0023-couchdb-changes-the-database-surface.md))* |
| `NATS.Net` version | Or n/a. Pin ≥ 3.0.1 |
| Owns AJAX endpoints? | Which backend endpoints the legacy pages call |
| Willing to recompile? | **This is the ADR-0012 answer** |
| Handles applicant data? | Class 3 exposure |
| Needs `application.id` correlation? | |
| **Path: agent or SDK** | Determination, below |
| Reasoning | One line. Why this path |

## The path determination

Apply Rev 3 **D2.9**'s rule to each 4.8 service and record the result with its
reasoning:

```
Does this service handle applicant data AND need correlation by application.id?

  ├── No  → AGENT   (default for most)
  └── Yes → SDK     (requires willingness to recompile)
```

Two things this determination does **not** settle:

🔒 **The capture-defaults review is not done here.** Rev 3 **D2.9** requires
agent candidates to be reviewed against Class 3 and 4 before use — *zero code
does not mean zero PII*, since the agent still captures URLs and log content by
its own defaults. 🔒 With CouchDB, URLs are the database surface
([ADR-0023](../adr/0023-couchdb-changes-the-database-surface.md)), so "captures
URLs" and "captures database access" are the same statement here. That review requires observing the agent, not
reading its documentation, so it belongs to Phase 2.

**A service unwilling to recompile cannot take the SDK path**, regardless of the
rule's outcome. If a service answers "yes" to the rule and "no" to recompilation,
record the conflict rather than resolving it silently — it is a decision for the
service owner, and it may mean accepting correlation by `trace_id` alone.

## Rollup — the answers this produces

**Estate composition:** ____ KYC services, ____ non-KYC services
→ ADR-0011: `tenant.id` promoted / withheld

**Recompilation:** ____ of ____ 4.8 services willing to recompile
→ ADR-0012: `net48` target justified / dead weight

**Paths:** ____ agent, ____ SDK, ____ conflicted

**`NATS.Net` below 3.0.1:** ____ services *(each needs an upgrade before Phase 1)*

**Failure classes absent from the estate** — anything Rev 3 assumed that does not
apply here.

## Source

**Confirmed 2026-08-10: nothing exists.** No service catalogue, no deployment
manifest, no CI project list. Nothing to transcribe. This is archaeology, and the
absence is recorded as a finding for the platform independent of this project.

Per [ADR-0021](../adr/0021-service-register-is-the-coverage-denominator.md), the
output of this worksheet is not a one-off list — it becomes the **service
register**, PR-gated in this repository, and it is the declared *services
expected* denominator of the Rev 3 **I4.6** coverage SLI. Without it, ADR-0009's
fail-closed governance has no detector.

Enumerate five sources independently, then reconcile:

| # | Source | Command or method | Catches |
|---|---|---|---|
| 1 | Azure DevOps | Pipeline and repository list | Anything with active CI |
| 2 | IIS hosts | `appcmd list sites`, `appcmd list apppools` | **The 4.8 services** — the ones least likely to have a pipeline |
| 3 | NATS | Connected clients, actively subscribed subjects | Async consumers with no HTTP surface |
| 4 | CouchDB | `_active_tasks`, `_stats`, and the HTTP access log by user agent | Anything reading or writing documents |
| 5 | Network | DNS entries, load-balancer backends, firewall egress rules | Anything reachable that the other four missed |

Anything appearing in one source and not the others is **interviewed to a named
owner**, not assumed away. Record which source each service was first found in —
a service found only by source 5 says something about the other four.

Source 2 is the load-bearing one. With no pipeline list to be incomplete, the
IIS hosts are the primary evidence for exactly the runtime whose services are
hardest to see.
