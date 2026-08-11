# 13. The Phase 0 abort criterion reorders the plan rather than stopping it

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D0.1** requires the last five incidents to be decomposed into
`detect → triage → diagnose → fix` with wall-clock time recorded per stage, and
attaches an abort criterion: if *detection* dominates, this project will not move
MTTR, so stop and build SLO-based alerting instead. Centralised telemetry only
improves the diagnose stage.

The reasoning is sound. The remedy is too blunt for our situation, for a reason
Rev 3 could not have known: substantial work has already been done here as
governance — the data classification, the allowlist, the correlation model, the
resource schema, the `service.name` convention.

SLO-based alerting is not an alternative that avoids this infrastructure. It
needs a collector, it needs `service.name` to be stable and conventional
([ADR-0006](./0006-service-identity-convention.md)), and Rev 3's own **D3.5**
places SLO definition inside this plan rather than outside it.

Rev 3 also gives no threshold for "dominates". A criterion without a number gets
its number chosen after the data is seen, which is not a criterion.

## Decision

The threshold is fixed before the decomposition is run: **detection exceeding
50% of total MTTR across the five incidents**.

If the threshold is crossed, the project does not abort. SLO-based alerting —
Rev 3 **D3.5** — is promoted from Phase 3 to Phase 1 and becomes the first
deliverable, ahead of estate-wide instrumentation.

The decomposition is still run, and its result is still recorded, whichever way
it falls.

## Consequences

- The insight Rev 3 was protecting is preserved: if the time is in detection,
  detection gets worked on first. What changes is that the shared foundation is
  not discarded to do it.
- Fixing the threshold in advance is the point. A criterion decided after seeing
  the data is a justification, not a criterion.
- Promoting SLO work forward costs schedule. Instrumentation coverage arrives
  later than the eight-week plan implies, and Gate 4's MTTR re-measurement moves
  with it.
- 🔒 This is a deliberate deviation from Rev 3, which states that Rev 3 wins
  where the two disagree. The deviation is recorded here rather than applied
  silently, and it is narrow: it changes the *remedy* attached to the criterion,
  not the criterion itself or the measurement behind it.
- Gate 4 still measures MTTR against the **D0.5** baseline. If detection
  dominated and SLO work was promoted, the honest reading at Gate 4 is that the
  improvement came from alerting rather than from telemetry — and Rev 3's warning
  against declaring success on coverage applies unchanged.
