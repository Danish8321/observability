# 21. The service register lives in this repository and is reconciled against reality

Date: 2026-08-10

## Status

Accepted

## Context

Confirmed 2026-08-10: **no service catalogue, deployment manifest, or CI project
list exists.** The estate is known by the people who work on it. Under ten
services makes enumeration tractable, but it is archaeology, and the absence is
itself a finding independent of this project.

Several decisions already taken assumed such a list existed:

- Rev 3 **I4.6** defines coverage as *services reporting ÷ services expected*,
  an SLI with a 100% target.
- [ADR-0009](./0009-governing-agent-instrumented-services.md) governs
  agent-instrumented services at the collector, fail-closed, and names the
  coverage SLI as the only external detector for a service that exports nothing.
- [ADR-0006](./0006-service-identity-convention.md) has no in-process validation
  for agent services, and leans on the same signal.

There is no source for the denominator. Without it the coverage SLI cannot be
computed, and both fail-closed designs lose the only thing that would notice
them failing.

Three homes for the list were considered.

**A markdown table in this repository, alone.** Version-controlled and PR-gated,
sitting beside the collector configuration that consumes it. It drifts the moment
someone deploys without updating it, and the drift is invisible.

**Derived from the collector's fail-closed enumeration.** One list, already
load-bearing. Its failure mode is the dangerous one: a service never added is
absent from both the list and the expectation, so coverage reads 100% while a
service is missing entirely. A metric that reports perfect health because it
cannot see what is missing is exactly the failure Rev 3 **I3.8** exists to
prevent.

**Deferred until something else owns a service registry.** Leaves coverage
uncomputable, and it is one of only two SLIs Rev 3 says does the real work.

## Decision

The service register is authored in this repository and PR-gated. It is the
declared value of *services expected*.

It is **reconciled against observed reality** on a recurring basis, and
discrepancies in **either** direction are findings:

- A service reporting telemetry that is not in the register — undeclared
  deployment.
- A service in the register that is not reporting — coverage failure, which is
  the SLI working as intended.

The register is not a Phase 0 worksheet that is filled in once. D0.3 creates it;
it is maintained thereafter.

Initial discovery, since nothing can be transcribed, enumerates and reconciles
five independent sources:

1. Azure DevOps pipelines and repositories
2. `appcmd list sites` and `appcmd list apppools` on each IIS host
3. NATS connected clients and actively subscribed subjects
4. SQL logins grouped by application
5. DNS entries, load-balancer backends, and firewall egress rules

Anything appearing in one source and not the others is interviewed to a named
owner rather than assumed away.

## Consequences

- The coverage SLI becomes computable, and with it ADR-0009's fail-closed
  detection and ADR-0006's external check for agent services.
- **The IIS enumeration is the load-bearing one.** A pipeline list would have
  missed the 4.8 services most of all; with no pipeline list at all, the hosts
  are the primary evidence for exactly the runtime whose services are hardest to
  see.
- D0.3 is a sized piece of work, not an afternoon of transcription. The estate
  inventory schedule reflects a five-source reconciliation.
- The register requires ongoing maintenance, and nothing yet enforces that a new
  deployment updates it. The reconciliation is what catches the omission rather
  than preventing it, so a gap is detected after the fact, not at deploy time.
- Reconciliation cannot run until telemetry is flowing, so between D0.3 and
  Phase 1 the register is unverified and rests on the quality of the discovery.
- That no register existed is recorded as a finding for the platform, separately
  from this project's deliverables.
