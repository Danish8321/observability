# 24. D0.3's five-source sweep is unattainable; working positions stand in its place

Date: 2026-08-24

## Status

Accepted

## Context

Rev 3 **D0.3** requires the estate inventory built by reconciling five
independent sources — Azure DevOps, IIS hosts, NATS monitoring, CouchDB
`_active_tasks`/`_stats`, and network/DNS/load-balancer records
([`phase0/estate-inventory.md`](../phase0/estate-inventory.md)).

Confirmed 2026-08-24 (danish): **none of the five sources are reachable at
all, and not as a staffing-lead-time or wrong-person-asking problem** —
there is no path to Azure DevOps pipeline data, no IIS host access, no NATS
monitoring endpoint, no CouchDB admin access, and no network/DNS/firewall
records available to this project, now or foreseeably. This is not "not yet
provisioned"; it is "not obtainable."

Two log exports danish shared 2026-08-24 (see
[`.scratch/estate-findings/`](../../.scratch/estate-findings/)) are the only
concrete estate evidence this project has ever had, and they arrived as a
one-off, not a queryable source — confirming the sweep's premise (nothing is
currently observed) rather than substituting for it.

This blocks more than D0.3 itself. [ADR-0011](./0011-estate-vocabulary-versus-domain-vocabulary.md)
(is the estate mixed?) and [ADR-0012](./0012-net10-first-sequencing.md) (will
any 4.8 service recompile?) were both left as **working positions, explicitly
not closed answers**, pending this sweep (recorded 2026-08-12,
`open-questions.md` Q6/Q7). [ADR-0021](./0021-service-register-is-the-coverage-denominator.md)
needs D0.3's output as the coverage SLI's denominator. **D0.2**'s performance
baseline needs D0.3's output to pick its three representative services. None
of these can wait on a sweep that will not happen.

## Decision

D0.3 is answered by **the working positions already recorded, promoted from
provisional to the estate inventory's answer of record**, not by a
source-reconciled register:

- Estate composition (Q6): uniformly KYC today; other services (KYC and
  non-KYC) onboarded once the KYC path is proven.
- 4.8 recompilation (Q7): deferred to last; .NET 10 proven first, 4.8
  decided after.
- Ownership (Q7c): danish owns everything by default.
- The two log-sighted services (`profile-api`, the compliance-backend/Ocelot
  pairing) stay recorded as provisional sightings in
  `phase0/estate-inventory.md` — real rows, not placeholders, but built from
  a single accidental source, not the five-source method.

The service register [ADR-0021](./0021-service-register-is-the-coverage-denominator.md)
depends on is **whatever gets named this way, plus whatever samples/ and
future onboarding work name going forward** — grown by accretion as services
are actually integrated, not produced by an upfront archaeology pass. This is
weaker than what Rev 3 asked for and is recorded as such, not smoothed over.

**D0.2** picks its three representative services from this same accreted
list rather than waiting on a completed register — "highest-throughput HTTP
API" still cannot be named without Run 0's own measurement, so the ordering
constraint in [ADR-0014](./0014-performance-baseline-method.md) is
unaffected; only the *source* of the candidate list changes.

## Consequences

- 🔒 This is a deliberate deviation from Rev 3, alongside the four already
  listed in [`docs/adr/README.md`](./README.md#deviations-from-rev-3) — Rev 3
  specifies a reconciled five-source register; this project has no method
  left to produce one.
- ADR-0011 and ADR-0012's working positions are no longer provisional pending
  D0.3 — they **are** the D0.3 answer, and carry whatever risk an
  unreconciled position carries: `tenant.id` promotion and the `net48` target
  are justified against a belief about the estate, not a verified count.
  Revisit either only if evidence surfaces that contradicts the working
  position, not on a schedule.
- The coverage SLI ADR-0021 defines has a **weaker denominator** than
  intended — an accreted, not exhaustively discovered, service list. A
  service nobody happens to integrate against or accidentally log-sight stays
  permanently invisible to the coverage detector. This is the same shape of
  gap ADR-0009 already accepts for agent-path services, now extended to the
  inventory itself.
- Q6/Q7/Q7c close in `open-questions.md` on this ADR rather than staying open
  pending a sweep that will not run.
- If access to any of the five sources becomes obtainable later, re-running
  the sweep still strictly improves on this — nothing here forecloses it.
