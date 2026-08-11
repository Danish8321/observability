# 20. Two access tiers: operational broad, audit restricted

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **I3.2** ends its defense-in-depth chain at `storage RBAC`, and **I3.3**
requires the audit pipeline to have separate retention *and* separate access
control. Neither is satisfied by an internal tool where everyone with an account
can read everything.

Production traces on this system carry `application.id`, `tenant.id`,
`correlation.id`, and `session.id` by design. Those are Class 2 — opaque, and
deliberately present, because [ADR-0007](./0007-correlation-lifetime.md)'s
correlation model is what makes an incident reconstructable.

The consequence is easy to miss: a trace store containing no Class 3 data is
still a behavioural record. Someone with read access can enumerate what an opaque
applicant identifier did, and when. The redaction working as designed does not
make the store low-value to an attacker.

A third tier was considered, restricting traces carrying Class 2 identifiers.
It was rejected because the people debugging a screening failure are exactly the
people who need `application.id`. That tier would either contain everyone anyway
or block the primary use case, and Rev 3's goal is reduced MTTR rather than
minimised access.

## Decision

Two tiers.

**Operational telemetry** — traces, metrics, logs. Readable by anyone on call or
debugging. Broad by intent.

**Audit pipeline** — a separate store, separate retention, and a materially
smaller group. Membership is not implied by engineering employment.

**Audit tier membership, decided 2026-08-10:** the compliance function, plus the
delegated technical owner named in
[ADR-0019](./0019-delegated-data-protection-ownership.md). Nobody else by
default.

The technical owner is included because that role approves the carve-out list,
new Class 2 identifiers, and the Gate 3 pre-production audit. Approving what may
be captured while being unable to see what was captured is a governance gap, not
a security improvement — the approval would rest on the description of the data
rather than the data.

Adding anyone else is a change to this ADR, countersigned per ADR-0019. Adding
on-call engineers as a standing group was rejected: a manually managed tier that
grows is the tier a leaver stays in.

The boundary between them is the one Rev 3 **I3.3** already requires, so this
adds a membership decision rather than a new mechanism.

## Consequences

- **I3.3** is satisfied with one boundary rather than several, and the
  diagnostic reach the project exists to provide is not reduced.
- 🔒 Anyone with operational access can enumerate an opaque applicant
  identifier's activity across the estate. This is a behavioural record, not an
  identity disclosure, and it is accepted deliberately. It is stated here so the
  data protection owner evaluates it under
  [ADR-0019](./0019-delegated-data-protection-ownership.md) rather than
  discovering it during the Gate 3 audit.
- This becomes a bake-off disqualifier: a store that cannot enforce the
  separation is out, regardless of how it performs otherwise. Recorded in
  [`docs/phase3/store-bakeoff.md`](../phase3/store-bakeoff.md).
- The regulatory request gains a statement covering it, so the position is
  confirmed rather than assumed —
  [`docs/regulatory-request.md`](../regulatory-request.md).
- **The audit tier is two to four people, so an audit question an engineer
  cannot answer becomes a request to one of them.** That friction is intended,
  and it will be felt during an incident. If it proves unworkable, the answer is
  an amendment here, not an informal account share.
- The technical owner sits in the audit tier *and* approves what enters it. No
  separation of duties exists at that scale; the compensating control is that
  approvals are pull requests in this repository, reviewable after the fact.
- With SSO confirmed not mandatory, membership is enforced by local accounts in
  whichever store wins, which makes joiner/leaver handling manual. A tier of
  four is small enough for that to be tractable and is the reason the tier is
  not larger. Deprovisioning is a bake-off disqualifier —
  [`docs/phase3/store-bakeoff.md`](../phase3/store-bakeoff.md).
- The invariant behind the split holds regardless of membership: operational
  telemetry is never the system of record for audit evidence. It may hold copies
  or references.
