# Store bake-off

**Status:** not started. Rev 3 **I0.6** shortlists; **I2.4** dual-writes;
**I3.11** decides.
**Candidates:** SigNoz Community (MIT, one system) · Grafana LGTM (AGPLv3, four
systems)

---

## The rule that governs this

Rev 3: **do not decide by scorecard.** Dual-write to both in Phase 2 and decide
on real traffic in Phase 3. The deciding question is *how many things does the
team operate at 3am?*

That is a sound instinct and an incomplete specification. "Evidence" with no
pre-agreed criteria becomes whichever tool the person running the bake-off
preferred at the start.

So, following the same logic that fixed the abort threshold in
[ADR-0013](../adr/0013-abort-criterion-becomes-a-reordering.md) **before** the
data was seen:

**Disqualifiers are agreed in advance. The tiebreak is the operational question,
argued in writing.**

A weighted scorecard is still rejected. Scorecards flatten "we would be operating
four systems instead of one" into a number that then loses to five minor
conveniences.

## Disqualifiers — agree before anything runs

Anything hitting one of these is out, regardless of how well it does elsewhere.

- [x] ~~Cannot satisfy the SSO requirement~~ — **does not apply.** SSO confirmed not mandatory (2026-08-10), so SigNoz Community's SSO gating does not disqualify it and the bake-off is a real comparison rather than a formality. Revisit if the position changes before **I3.11**
- [ ] **Cannot enforce the two access tiers** in [ADR-0020](../adr/0020-telemetry-access-tiers.md) — operational broad, audit restricted — as Rev 3 **I3.3** requires
- [ ] **Cannot deprovision access reliably.** With SSO confirmed not mandatory, the tiers are enforced by each store's local accounts and groups rather than by a directory. That makes joiner/leaver handling manual, and the audit tier is the smaller and more sensitive of the two. Test how each store handles removing a person, not just adding one
- [ ] **Cannot ingest at measured peak volume without loss**
- [ ] **Restore from backup fails, or the procedure is undocumented.** Rev 3 **I3.7** — a backup that has never been restored is not a backup
- [ ] **Licensing prohibits an intended use.** The AGPL position in **I0.6** is fine for internal use and becomes a live question the moment anyone proposes embedding dashboards in a customer-facing portal

## What both stores are asked

The same questions, from
[`../diagnostic-queries.md`](../diagnostic-queries.md). Two stores cannot be
compared unless they are asked to answer identically.

Build the four dashboards in the store being evaluated, from those
specifications. Rev 3 **I4.3** allows four, no more.

## Evidence to record — not scored, just recorded

| Area | What to write down |
|---|---|
| Ingest | Sustained rate achieved, loss observed, resource cost |
| Query | Latency for each specification, and which were awkward or impossible to express |
| Operations | Components to run, upgrade path, schema migration path, what failed and how it was diagnosed |
| Storage | Actual compression achieved against the **I3.4** planning assumption of roughly 10× |
| Cold tier | Whether a query against archival storage actually returns |
| Failure | Behaviour during the **I3.6** failure matrix |

Rev 3 **I3.4** is explicit that storage is not the constraint at this scale and
must not drive the decision. Sample for ingest CPU and query performance, never
for storage cost.

## The decision

If both survive the disqualifiers, the tiebreak is the operational question,
written down rather than discussed: **how many things does the team operate at
3am, and what happens when the person who set it up is on holiday?**

Record the reasoning, not just the outcome. Rev 3 **Phase 5** requires an honest
re-opening of the managed-SaaS option if the stack exceeds roughly 0.25 FTE for
two consecutive quarters — and that review is only possible if the original
reasoning survives in writing.

## Consequence of the decision

Dashboards are rebuilt from the specifications if the losing store was the one
already built in. That cost is transcription, not rediscovery, which is the point
of ADR-0016.
