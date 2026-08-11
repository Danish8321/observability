# 19. Data protection ownership is delegated technically and countersigned

Date: 2026-08-10

## Status

Accepted — pending the two names.

## Context

Rev 3 **Gate 3** requires the PII audit to be signed off by the data protection
owner, and **D3.1** makes exceptions to `SetDbStatementForText = false` available
only by written approval. Both require a person. No one is currently named, and
Phase 4 is gated on Gate 3.

The judgement the role has to exercise here is unusually technical. The
compliance argument in this design does not rest on a policy statement; it rests
on the carve-out list in [ADR-0018](./0018-allowlist-composition.md) — on knowing
that `http.request.header.*` contains `Authorization`, that `db.query.text` is
what statement suppression exists to prevent, and that a family allow is broad by
intent. A data protection officer who is not close to telemetry cannot
meaningfully approve that list, and approving it without understanding it is
worse than not approving it, because it produces a signature that means nothing.

Equally, a technical owner alone cannot carry regulatory accountability for a
KYC system.

## Decision

Two roles, both named, with distinct objects of approval.

**Delegated technical owner** — approves the artifacts whose correctness is a
technical judgement:

- the carve-out list (ADR-0018) and any addition to it
- new allowlist keys and new Class 2 identifiers
- metric dimensions, under Rev 3 **D2.1** rule 1
- baggage keys, under **D2.1** rule 3, which default to disabled
- the D3.1 audit's technical findings on both runtimes

**Data protection officer** — countersigns, and owns outright:

- the Gate 3 sign-off itself
- any exception to `SetDbStatementForText = false`, per **D3.1**
- the regulatory assumptions in
  [ADR-0015](./0015-regulatory-assumptions-pending-written-answer.md) once the
  I0.1 answer arrives
- the quarterly PII and cardinality re-audits in Phase 5

Where the two disagree, the countersignature is withheld and the change does not
proceed. Neither role can approve alone.

## Consequences

- The signature at Gate 3 means something, because the person producing the
  technical judgement understands the artifact and the person carrying
  accountability is not asked to rubber-stamp what they cannot evaluate.
- Two approvals are slower than one. This is accepted: Rev 3 **D2.1** already
  requires a baggage key and a metric dimension to receive the same review as any
  other governance change, so the latency is by design rather than overhead.
- 🔒 The delegated technical owner is a single point of failure for the quality
  of the compliance argument. The mitigation is that their approvals are recorded
  as pull requests in this repository and are therefore reviewable after the
  fact, not that a second technical reviewer exists.
- This ADR is incomplete until both names are recorded. Until then Gate 3 remains
  blocked, and the block is a staffing dependency with lead time rather than a
  technical one.
