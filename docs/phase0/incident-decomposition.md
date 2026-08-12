# D0.1 — Incident decomposition

**Status:** answered 2026-08-12 — no data exists, see "Reading the result" below
**Budget:** one hour. Rev 3 calls this the single most valuable hour in the plan.
**Threshold:** fixed in advance by [ADR-0013](../adr/0013-abort-criterion-becomes-a-reordering.md) — do not revisit it after seeing the data.

---

## Why

MTTR is a sum of four stages. Centralised telemetry improves one of them.

| Stage | From → to | Improved by this project? |
|---|---|---|
| Detect | first bad event → a human knows | **No.** That is alerting and SLOs. |
| Triage | knows → knows which service or component | Partly — coverage and dashboards help. |
| Diagnose | knows which → knows the cause | **Yes.** This is the value. |
| Fix | knows cause → resolved | No. That is deploy speed and code. |

If the hours are sitting in detection, this platform can be built perfectly,
pass every gate, and MTTR will not move — because the time is in a stage it does
not touch.

## Sample

The **last five incidents chronologically**. Not selected, not curated — whatever
happened. Selection would introduce the opinion of the selector, who already has
a view on whether this project should proceed.

Then record which failure classes the sample does **not** contain. If no async
or messaging incident appears, that matters: Rev 3 **D3.6**'s async metrics
would then be justified by assumption rather than evidence, and that should be
stated rather than assumed away.

## Method

For each incident recover four timestamps from whatever record exists — ticket
history, chat scrollback, alert firing times, deploy logs, commit times.

| Mark | Meaning |
|---|---|
| `t0` | the first bad event actually occurred |
| `t1` | a human knew something was wrong |
| `t2` | the failing service or component was identified |
| `t3` | the cause was understood |
| `t4` | resolved |

Then `detect = t1−t0`, `triage = t2−t1`, `diagnose = t3−t2`, `fix = t4−t3`.

**`t0` is almost always a guess.** Guess it and mark it as a guess. A guessed
`t0` still separates "detection took four minutes" from "detection took nine
hours", which is the only precision the threshold needs.

**"We found out when a customer called" means detection dominates**, whether or
not anyone wrote it down that way.

## Worksheet

| # | Date | One-line description | `t0` (guess?) | `t1` | `t2` | `t3` | `t4` | detect | triage | diagnose | fix |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | | | | | | | | | | | |
| 2 | | | | | | | | | | | |
| 3 | | | | | | | | | | | |
| 4 | | | | | | | | | | | |
| 5 | | | | | | | | | | | |
| | | | | | | | **totals** | | | | |

**Failure classes absent from this sample:**

*(e.g. async backlog, dependency failure, data corruption, performance
regression, security event)*

## Reading the result

Compute detection as a share of total MTTR across all five.

**Detection ≤ 50%** — proceed as planned. The time is in stages this project
improves.

**Detection > 50%** — per ADR-0013 the project does **not** abort. SLO-based
alerting (Rev 3 **D3.5**) is promoted from Phase 3 to Phase 1 and becomes the
first deliverable, ahead of estate-wide instrumentation. The collector and the
`service.name` convention are needed either way, so nothing already decided is
discarded.

Either way, record the result. Rev 3 **Gate 4** re-measures MTTR against the
**D0.5** baseline, and the honest reading there depends on knowing which stage
the improvement came from.

## If the data does not exist

If incidents are not recorded anywhere with timestamps, this decomposition
cannot be done retrospectively.

That is itself a finding, and a significant one: it means detect and triage time
are currently unmeasurable, which is a stronger argument for SLO-based alerting
than any number this worksheet could have produced. Record it as the result and
apply the ADR-0013 reordering.

## Result (2026-08-12)

**Confirmed: this data does not exist.** No incident record anywhere in the
estate carries the four timestamps this worksheet needs — no ticket system,
chat scrollback, or alert history reconstructs `t0`–`t4` for even one past
incident, let alone the last five. This mirrors Q5b's finding for the
performance baseline (no per-service throughput figures exist either) — the
estate currently has no historical telemetry of any kind to retrofit, which
is the condition this whole project exists to fix.

Per the worksheet's own escape clause: detect and triage time are therefore
unmeasurable today. Applying ADR-0013's reordering — SLO-based alerting
(Rev 3 D3.5) is promoted from Phase 3 to Phase 1, becoming the first
deliverable ahead of estate-wide instrumentation. The collector and
`service.name` convention are needed either way, so nothing already decided
is discarded.

Gate 4's re-measurement of MTTR against the D0.5 baseline should record that
D0.1 had no retrospective baseline to compare against — only whatever D0.5
(Q9) establishes going forward, once this platform starts producing incidents
with real timestamps.
