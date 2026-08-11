# 14. Performance baselines are taken now, on both runtimes, by a repeatable script

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D0.2** requires a performance baseline recorded with telemetry off — p50,
p95, p99 latency, CPU, working set, allocation rate, throughput — and warns it
cannot be reconstructed later.

That warning is stronger than it first appears. `OTEL_SDK_DISABLED=true` is not
equivalent to never-instrumented: the packages are still loaded, `ActivitySource`
instances still exist, instrumentation hooks are still registered, and the
sampler is still consulted. It is close to the baseline and not identical to it,
and the size of the gap cannot be established without the number that was not
taken.

Four things depend on the baseline. Rev 3 **D3.3**'s ~5% overhead tripwire,
**Gate 3**'s requirement that the performance budget be measured across three
configurations, and most sharply **D4.2**, whose rollback thresholds — p99
regression over 10%, CPU over 10%, memory over 15% — are expressed as numbers
specifically so that rollback is not a judgement call. Without a baseline they
become a judgement call, during a production rollout.

A baseline taken under unrepresentative load is worse than none, because it
produces false confidence in those same numbers.

## Decision

**Method.** A repeatable synthetic load script produces the baseline, and the
identical script is re-run at **D3.3**. The script is the measurement instrument
on both occasions.

**Amended 2026-08-10.** Passive production observation was specified as a check
alongside the synthetic runs. Confirmed since: **no per-service throughput
figures exist anywhere in the estate**, so the synthetic script has no rate to
run at. Passive observation is therefore promoted to **Run 0**, runs first over
a fixed one-week window agreed in advance, and supplies the rate. It remains the
check as well — if the synthetic run at the measured rate does not reproduce
Run 0's CPU and latency, the script is wrong.

This also reorders Phase 0: **D0.3 precedes D0.2**, because the three
representative services cannot be selected without the register, and
"highest-throughput HTTP API" cannot be named before throughput is measured. The
expiry pressure is unaffected — nothing is instrumented until Phase 2 — but the
sequence is no longer the one Rev 3 numbers imply.

At Phase 3, where instrumented and uninstrumented instances can run behind the
same load balancer, that A/B comparison supersedes the synthetic delta — same
hardware, same traffic, same hour.

**Scope.** Three representative services: the highest-throughput HTTP API, the
NATS consumer with the deepest processing, and one .NET Framework 4.8 MVC
application.

**Timing.** All three baselines are taken now, including the 4.8 one, even though
[ADR-0012](./0012-net10-first-sequencing.md) defers 4.8 work to Phase 2.

## Consequences

- Staging hardware and query mix differ from production, so the synthetic
  baseline's absolute numbers are wrong while the delta it measures is right. The
  passive production recording exists so that the difference is known rather than
  assumed.
- Run 0's sources — IIS logs, Windows performance counters, the NATS monitoring
  endpoint, SQL DMVs — measure each service in isolation and correlate none of
  them. That is a limitation of the baseline and simultaneously the clearest
  available statement of the problem this project exists to solve.
- A one-week window costs a week of elapsed time before any synthetic run, and
  a week that turns out to be atypical is recorded as such rather than reused
  silently.
- Taking the 4.8 baseline now is the only item in this repository's decisions
  that expires. Everything else can be decided later at the cost of rework; this
  one cannot be recovered once those services are instrumented.
- The 4.8 measurement is non-invasive — no code, no recompile, no deploy — so it
  does not contradict ADR-0012's deferral of 4.8 *work*. What is deferred is
  instrumentation, not observation.
- 🔒 Without this, the estate's highest-risk services (Rev 3 **F-I5**) would be
  rolled out to production against **D4.2** thresholds that had no denominator.
- Rev 3 **F-I3** complicates the 4.8 measurement: app-pool recycles and idle
  timeouts affect working set and throughput readings. The measurement must
  record pool configuration alongside the numbers, or the Phase 3 comparison will
  not be like for like.
