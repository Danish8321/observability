Status: closed — 2026-09-08

# Two published documents asserted enforcement behaviour that was not true

Both were written by reading the specification rather than the running system,
which is the same failure mode as an allowlist tightened against a document
(ADR-0018) — here applied to the documents themselves.

**`docs/onboarding/backend-and-dashboards.md`** (commit `5625b44`) gave as
dashboard 3's first blocker:

> `messaging.*` is admitted only key-by-key … Until it is done, these dimensions
> are dropped in-process and the panels are empty by construction.

False in both halves. `messaging.` was allowed as a family prefix (issue 01), so
nothing was dropped; and naming the allowlist as the blocker hid the real one,
which is that nothing emits consumer-group data at all. A reader acting on that
paragraph would have enumerated keys and found the panels still empty.

**`deploy/README.md`** said the demo stack "has no allowlist enforcement" and
left *"Allowlist processor generated and attached at the collector"* as an
unticked box. The allowlist has been attached and held against
`AllowlistRules.cs` by `contract.sh` for some time. It is also hand-written
rather than generated, so the checkbox described a thing that was never going to
happen in that form.

## Impact

Documentation-only, and both are onboarding documents — the audience is someone
with no other source of truth about the system. The dashboard claim would have
sent a reader to the wrong work. The `deploy/README.md` claim understated the
protection actually in place, which is the safer direction to be wrong but
still wrong.

## Fix

Dashboard 3's blocker list rewritten to name the sample's messaging model
(issue 04). `deploy/README.md` corrected, the checkbox ticked with what actually
holds it (`transform/allowlist`, hand-written, `contract.sh`) rather than the
wording that was never accurate.

## Comments

The general lesson is cheap to state and was expensive here: a claim about what
an enforcement point does is checkable in one `grep` of `AllowlistRules.cs` or
one `e2e-instrumented.sh` run. Neither was done before publishing.

