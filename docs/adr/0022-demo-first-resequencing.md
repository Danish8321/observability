# 22. A demo precedes Phase 0; compliance work is deferred but not cancelled

Date: 2026-08-10

## Status

Accepted — supersedes the sequencing of Rev 3 Phase 0, not its content

## Context

Every decision in this repository so far assumed the project was already
approved and that the question was how to build it correctly. It is not
approved. Stakeholders have not agreed the problem is worth solving, and the
plan as written asks for roughly eight weeks of compliance and baseline work
before anything observable exists.

That ordering is defensible when the mandate exists. Here it inverts the risk:
the most expensive, least demonstrable work runs first, and if the answer is
"not worth it" the whole cost is already sunk.

Two facts change the calculation:

- **The organisation owns the data and has a compliance function.** The
  compliance questions are real and have an owner who is not this project. They
  do not have to be answered before anything is built.
- **The demo can run on staging or synthetic data.** Most of what the compliance
  work protects against only arises when production KYC traffic reaches a
  telemetry store.

## Decision

**Build an 8-day demo first.** Plan at [`../demo/plan.md`](../demo/plan.md).

The demo proves one thing: that a failure spanning several services is diagnosed
faster with this stack than without it, demonstrated live on the clock.

**Deferred:** the regulatory request (I0.1), the DPO briefing and Gate 3
sign-off (ADR-0019), access tiers (ADR-0020), the allowlist analyzer and runtime
enforcement (ADR-0002, 0003, 0017, 0018), the store bake-off (ADR-0016), the
one-week performance baseline (ADR-0014), the full estate inventory and service
register (ADR-0021).

**Not deferred, and the boundary this decision rests on:** the demo does not
touch production KYC traffic. Nothing else in the deferred list is irreversible;
this one is. Telemetry written to a store is written, and a CPR discovered in it
afterwards is a data-deletion problem rather than a configuration change.

**Also not deferred, because it expires:** a one-hour performance snapshot of
the demo services before they are instrumented. ADR-0014's full method is
deferred; the cheap irreversible part of it is not.

**Also not deferred, because CouchDB moved it:** the identity-in-names audit is
narrowed to one question — are CouchDB document IDs derived from applicant data
or opaque — and answered in Phase D0. See
[ADR-0023](./0023-couchdb-changes-the-database-surface.md).

The store is chosen as SigNoz for the demo without a bake-off. This is
reversible at the cost of rebuilding dashboards, which
[ADR-0016](./0016-diagnostic-queries-are-the-durable-asset.md) already
anticipated by specifying queries store-neutrally.

## Consequences

- If the answer is "not worth it", the project cost 8 days rather than two
  months. That is the decision's entire purpose and it is worth stating plainly.
- If the answer is "yes", the compliance work starts then and runs **in parallel
  with** the build rather than ahead of it. The eight-week horizon is absorbed
  rather than paid twice — but it is still eight weeks, and it still gates
  production traffic. The demo does not shorten it; it moves it.
- **Every deferred ADR remains Accepted and unimplemented.** They are not
  provisional, not drafts, and not reopened by this decision. The register of
  what is deferred lives in this ADR so that "we never did that" is
  distinguishable from "we decided not to yet."
- Demo scope is dictated by the chosen failure scenario and nothing else. Any
  work not needed to diagnose that failure live is out, including work this
  repository has already specified in detail.
- Building on staging means the demo's numbers are staging numbers. The
  comparison it demonstrates — same failure, two methods — is valid regardless,
  because both sides of it run in the same place.
- The risk this accepts: a demo that succeeds creates pressure to put production
  traffic through the same stack immediately, with the enforcement layer still
  unbuilt. The boundary above is the answer, and it is a decision that will have
  to be defended under enthusiasm rather than under scrutiny.
