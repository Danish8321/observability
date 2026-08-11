# Phase 0 — decide and baseline

Rev 3's week 0. No code is written. The purpose is to know whether this project
is worth doing, and what "better" will mean.

Everything here is a worksheet: method decided, data not yet collected.

## Order of work

**Reordered 2026-08-10.** The baseline was first. It cannot be, because no
service catalogue and no throughput figures exist: the baseline's three services
cannot be selected without the register, and its synthetic runs have no rate
without a week of passive observation. See
[ADR-0021](../adr/0021-service-register-is-the-coverage-denominator.md) and the
amendment to [ADR-0014](../adr/0014-performance-baseline-method.md).

| # | Worksheet | Why this order |
|---|---|---|
| 1 | [Estate inventory](./estate-inventory.md) | Nothing to transcribe — five-source reconciliation. Produces the register everything else selects from, and unblocks ADR-0011 and ADR-0012 |
| 2 | [Performance baseline](./performance-baseline.md) | **Expires.** Run 0 is a fixed one-week window and supplies the rate for Runs 1 and 2. Start the week as soon as (1) names the services |
| 3 | [Incident decomposition](./incident-decomposition.md) | Carries the criterion that reorders the plan. Rev 3 calls it the most valuable hour. Runs during Run 0's week — it costs interviews, not machine time |
| 4 | [MTTR baseline](./mttr-baseline.md) | Same five incidents as (3) |
| 5 | [Identity in names audit](./identity-in-names-audit.md) | A reading exercise now, a data-deletion exercise after Phase 2. Its inputs are routes and subjects, both enumerated by (1) |

Items 2 and 5 get more expensive with delay: (2) becomes impossible once anything
is instrumented, (5) becomes a deletion problem. Neither can start before (1),
which makes the inventory the critical path for the whole phase and not the
paperwork it looks like.

Items 3 and 4 run in parallel with (2), since one consumes people's time and the
other consumes a calendar week.

## Not covered here

Rev 3's Phase 0 infrastructure track — log volume measurement, host sizing, DNS
and TLS, the configuration repository, the store shortlist — belongs to the
platform side and is not worksheeted here.

The regulatory request, Rev 3 **I0.1**, is drafted at
[`../regulatory-request.md`](../regulatory-request.md) and is not yet sent.

## Gate 0

- [ ] Incident decomposition complete; reordering decision made per ADR-0013
- [ ] Performance baseline recorded, telemetry off, both runtimes
- [ ] Baseline MTTR recorded
- [ ] Estate inventory complete — runtime, transport, recompile-willingness, path
- [ ] Service register committed and PR-gated — the coverage denominator, ADR-0021
- [ ] `NATS.Net` versions known
- [ ] 🔒 Regulatory constraints documented and signed
- [ ] Volume measured; hosts requested and sited compliantly
- [ ] Configuration repository created

What is still unanswered across all of these is tracked in
[`../open-questions.md`](../open-questions.md), not in anyone's head.
