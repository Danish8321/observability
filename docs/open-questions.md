# Open questions

Live register. Everything here is unresolved. Decisions that have been made live
in [`docs/adr/`](./adr/) and are not repeated.

An item leaves this file only when it is answered, and the answer lands in an
ADR or in the plan it belongs to — never by being deleted.

**Resequenced 2026-08-10 by [ADR-0022](./adr/0022-demo-first-resequencing.md).**
A demo comes first. Q1, Q2, Q3b and the Phase 0 rows below are **deferred, not
closed** — they gate production traffic, not the demo. Demo-blocking questions
are in their own section at the top.

## Blocking the demo

| # | Question | Blocks | Owner |
|---|---|---|---|
| ~~QD1~~ | **Answered 2026-08-10: no historical incident.** Integrate into the real application, run end to end on dummy data, demo that. Guide at [`demo/integration.md`](./demo/integration.md) | — | closed |
| ~~QD1b~~ | **Answered 2026-08-11: `ApplicationId` containing `"fail"`.** Already coded in `Screening.Domain/ScreeningService.cs` — throws `ScreeningProviderException`, `Screening.Worker` retries 3×, exhausts, and abandons. API returns 202 immediately; `GET /applications/{id}` shows `Received` forever; the trace carries all 3 retry events plus the abandon tag; the `Abandoned` counter increments by reason. No new code, no redeploy. Confirmed: this is a business-logic exception, not a telemetry failure — the app never crashes because of observability itself (Rev 3 I3.6, enforced independently of this fault) | — | closed |
| ~~QD2~~ | **Answered 2026-08-11: opaque.** CouchDB document IDs are not derived from applicant data. The exposure ADR-0023 flagged (identity leaking via `url.full`) does not apply. `CouchDbUrlPolicy` redaction stays in as defense-in-depth, not as a compliance-blocking fix | — | closed |
| ~~QD3~~ | **Answered 2026-08-11: no staging — Dev first.** The demo runs against Dev, not a staging environment reproducing production. Satisfies the ADR-0022 boundary (no production KYC traffic) trivially, since Dev isn't production | — | closed |

## Blocking production, deferred until after the demo

Work cannot correctly proceed past the named point while these are open. A
working assumption recorded here is not an answer — it is a position held until
the answer arrives, and the row stays until it does.

| # | Question | Blocks | Owner |
|---|---|---|---|
| ~~Q1~~ | **Answered 2026-08-11: both roles held by the Architect (danish).** This is a deliberate deviation from ADR-0019's separation-of-duties design — recorded there as a residual risk, not a closed question. No second person currently reviews Gate 3 sign-off. Brief at [`data-protection-brief.md`](./data-protection-brief.md) still not sent — send it to whoever eventually takes the DPO half if the roles split | — | closed (with residual risk) |
| Q2 | What is the written regulatory answer? Request drafted at [`regulatory-request.md`](./regulatory-request.md) — **not yet sent, recipient unconfirmed**. | Confirms or refutes the five assumptions in [ADR-0015](./adr/0015-regulatory-assumptions-pending-written-answer.md) | — |
| ~~Q3~~ | **Answered 2026-08-10: SSO is not mandatory.** Both candidate stores remain in contention; the SSO disqualifier does not apply | — | closed |
| ~~Q3b~~ | **Answered 2026-08-10: compliance function plus the ADR-0019 technical owner.** [ADR-0020](./adr/0020-telemetry-access-tiers.md) amended; the disqualifier is now testable. Names still pending via Q1 | — | closed |

## Awaiting Phase 0

These are answered by work Rev 3 already schedules, and no decision here should
front-run them.

| # | Question | Source | Consumes |
|---|---|---|---|
| Q4 | Where does incident time actually go — detect, triage, diagnose, fix? Worksheet ready at [`phase0/incident-decomposition.md`](./phase0/incident-decomposition.md), **not started** | D0.1 | Threshold already fixed at 50% by [ADR-0013](./adr/0013-abort-criterion-becomes-a-reordering.md), so the answer cannot be argued after the fact |
| Q5 | What are the performance baselines with telemetry off? Plan ready at [`phase0/performance-baseline.md`](./phase0/performance-baseline.md), **not started** | D0.2 | Method fixed by [ADR-0014](./adr/0014-performance-baseline-method.md). **Expires** — unrecoverable once instrumented. Run this first |
| ~~Q5b~~ | **Answered 2026-08-10: no figures exist.** Passive observation promoted to Run 0, one fixed business week, and it supplies the rate. [ADR-0014](./adr/0014-performance-baseline-method.md) amended; **D0.3 now precedes D0.2** | — | closed |
| Q5c | Which week is the observation window, and is it representative? | D0.2 | Fixed in advance or the baseline is "whenever we looked." Nobody currently knows the estate's peak day — that is itself a Run 0 output |
| Q6 | Is the estate mixed, or uniformly KYC? Worksheet at [`phase0/estate-inventory.md`](./phase0/estate-inventory.md), **not started** | D0.3 | [ADR-0011](./adr/0011-estate-vocabulary-versus-domain-vocabulary.md) — `tenant.id` is withheld from both packages until answered |
| Q7 | Will any 4.8 service be recompiled, or is the agent path universal? Same worksheet | D0.3 | If universal, the `net48` target has no purpose and [ADR-0012](./adr/0012-net10-first-sequencing.md) and ADR-0001 are revisited |
| ~~Q7b~~ | **Answered 2026-08-10: nothing exists.** The inventory is archaeology, five-source reconciliation, and its output becomes the maintained service register per [ADR-0021](./adr/0021-service-register-is-the-coverage-denominator.md) | — | closed |
| Q7c | Who owns each service once discovery names them? Discovery finds processes; it does not find people | D0.3 | Reconciliation discrepancies are interviewed to a named owner. Without owners, an unaccounted-for service has nobody to ask |
| Q8 | Do any route templates or NATS subjects encode identity? Worksheet at [`phase0/identity-in-names-audit.md`](./phase0/identity-in-names-audit.md), **not started** | D0.4b, D2.2 | A reading exercise now; a data-deletion exercise after Phase 2. 🔒 A finding here is an existing exposure, not a project task |
| Q9 | What is the baseline MTTR? Worksheet at [`phase0/mttr-baseline.md`](./phase0/mttr-baseline.md), **not started** | D0.5 | The acceptance criterion for the whole project at Gate 4 |

## Awaiting Phase 1 or later

| # | Question | Earliest answerable | Consumes |
|---|---|---|---|
| Q10 | Which attribute keys does the instrumentation actually emit? Families and carve-outs drafted at [`allowlist.md`](./allowlist.md); **empirical validation pending** | Phase 1 fixture | Empirical half of [ADR-0018](./adr/0018-allowlist-composition.md); reconciliation is a Gate 2 item |
| Q10b | Which semconv version is pinned? | When the allowlist is first declared in code | Stable families by prefix; `messaging.*` enumerated individually |
| Q11 | Do the three 4.8 failure modes behave as the guide documents? Procedure at [`phase2/framework-failure-validation.md`](./phase2/framework-failure-validation.md), **not started** | Phase 2 | [ADR-0005](./adr/0005-enforcing-the-framework-wiring.md) — its rules are not implemented until observed |
| Q11b | Is the gRPC exporter failure observable at all — connection, log line, internal metric? | Phase 2 fixture | If yes, it becomes a stack-health signal rather than a manual Gate 1 check, and ADR-0005 is revised |
| Q12 | Which store wins the bake-off? Criteria and disqualifiers at [`phase3/store-bakeoff.md`](./phase3/store-bakeoff.md); query specifications at [`diagnostic-queries.md`](./diagnostic-queries.md) | Phase 3, I3.11 | Disqualifiers must be agreed **before** the bake-off runs. Two of them depend on Q3 and Q3b |
| Q13 | What is the real durability bound when the backend is down? Procedure at [`phase3/failure-matrix.md`](./phase3/failure-matrix.md), **not started** | Phase 3, I3.6 | The bound is chosen — queue capacity sized from the measured restore window — then verified. I3.7 runs first |

## Accepted risks

Not questions. Decisions whose downside is known and taken deliberately.

- **Agent-instrumented services are unvalidated in-process.** No library runs
  there, so D2.5's fail-fast cannot apply. Detection is external, via the
  coverage SLI. [ADR-0006](./adr/0006-service-identity-convention.md),
  [ADR-0009](./adr/0009-governing-agent-instrumented-services.md).
- **Exception messages are covered only by collector pattern scanning**, which
  catches shapes it knows and will miss novel ones.
  [ADR-0004](./adr/0004-free-text-telemetry-and-exceptions.md).
- **Allowlist families are permissive within their prefix.** The compliance
  argument rests on the carve-outs being right.
  [ADR-0018](./adr/0018-allowlist-composition.md).
- **Dashboard specifications can drift from deployed dashboards.** Nothing
  currently checks. [ADR-0016](./adr/0016-diagnostic-queries-are-the-durable-asset.md).
- **Provenance defends against accident, not a determined developer**, who can
  fork the package or emit raw OTLP.
  [ADR-0017](./adr/0017-allowlist-declared-as-assembly-attributes.md).
- **Both ADR-0019 roles are held by one person** (the Architect), so there is
  currently no independent countersignature on Gate 3, D3.1 exceptions, or the
  ADR-0015 regulatory assumptions. Accepted 2026-08-11 as a staffing-lead-time
  problem, not a design one.
  [ADR-0019](./adr/0019-delegated-data-protection-ownership.md).
