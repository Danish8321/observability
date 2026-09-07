# 27. D0.2's Run 0 has no data source — there is no baseline to take, ever

## Status

Accepted

## Context

[`.scratch/d0.2-access-check.md`](../../.scratch/d0.2-access-check.md) checked
D0.2's six Run 0 signals against
[ADR-0024](./0024-estate-inventory-by-working-position-not-sweep.md)'s
findings, since D0.2's needs only partially overlap D0.3's five sources: log
*files* rather than host access, `_stats` rather than full CouchDB admin, and
two signals (perf counters, GC) ADR-0024 never covered at all.

Confirmed 2026-08-30 (danish), against all four open asks in the access-check:

- IIS/Kestrel access logs for the Run 0 candidate services: **not exportable
  as files**, even without host access.
- Windows performance counters (CPU, working set, GC): **not reachable**,
  remotely or ad hoc.
- CouchDB `_stats`: **not reachable** without full admin.
- NATS monitoring endpoint: **confirmed unreachable**, carrying ADR-0024's
  finding forward.

This is a stronger result than ADR-0024's. ADR-0024 found D0.3's
five-*source* register unattainable but left working positions to stand in.
D0.2's Run 0 has no such fallback: it is "passive production observation," and
every one of its six signals routes through a source that is now confirmed
closed. There is nothing to observe passively with.

## Decision

D0.2's Run 0 is **unattainable, not merely blocked pending access.** This
propagates:

- [`phase0/performance-baseline.md`](../phase0/performance-baseline.md)'s
  framing changes from "not started" (implying it will start once scheduled)
  to **no baseline exists, and none can be taken under current access** — not
  "not yet taken."
- Runs 1 and 2 (synthetic load, ramp to saturation) are unaffected by this
  ADR — they don't depend on Run 0's sources — but they lose their
  cross-check: ADR-0014's method compares Run 1 back against Run 0 to catch a
  wrong synthetic rate. With no Run 0, that comparison cannot happen, and the
  synthetic rate Runs 1/2 use has to be justified another way (estate belief,
  per ADR-0024's accreted candidate list) with no way to confirm it.
- `open-questions.md` Q5c closes on this ADR rather than staying open pending
  further asks — the four questions in the access-check were the narrowest
  version of the ask, and all four came back "no."

## Consequences

- 🔒 D3.3's ~5% overhead tripwire and D4.2's rollback thresholds (p99 > 10%,
  CPU > 10%, memory > 15%) now have **no production-observed baseline to
  compare against**, only Runs 1/2's synthetic numbers, taken on trust rather
  than confirmed against reality. This is a materially weaker safety net for
  the one gate (D4.2) explicitly designed to remove judgement calls from a
  production rollout.
- This does not block Runs 1/2 or D3.3 itself — the script-based method
  stands on its own — but the risk it carries should be named at Gate 3
  rather than discovered there.
- Same posture as ADR-0024: revisit only if access changes, not on a
  schedule. If any one of the four sources becomes reachable later, that
  alone doesn't restore Run 0 — the other three still block it — but is
  worth re-checking against this ADR.
- Consistent with the existing deviations list
  ([`README.md`](./README.md#deviations-from-rev-3)): like ADR-0024, this is
  an estate-access fact, not a project choice, and Rev 3's D0.2 requirement
  cannot be met as written.
