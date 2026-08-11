# D0.2 — Performance baseline

**Status:** not started
**Method fixed by:** [ADR-0014](../adr/0014-performance-baseline-method.md)
**Priority:** first among *measurements*. This is the only measurement in the
programme that expires.
**Ordering:** [D0.3](./estate-inventory.md) runs before this. Confirmed
2026-08-10 that no throughput figures exist, so Run 0 supplies them — and Run 0
cannot pick its three services without the register.

---

## Why this comes first

Right now nothing is instrumented, so "telemetry off" is free and exact — it is
simply the current state. After Phase 2 it is unrecoverable.

`OTEL_SDK_DISABLED=true` is **not** equivalent to never-instrumented: packages
are loaded, `ActivitySource` instances exist, instrumentation hooks are
registered, and the sampler is still consulted. It is close, and the size of the
gap cannot be established without the number that was not taken.

Four things consume this baseline:

| Consumer | Needs |
|---|---|
| Rev 3 **D3.3** | Overhead across three configurations, against a ~5% tripwire |
| Rev 3 **D4.2** | Rollback thresholds: p99 > 10%, CPU > 10%, memory > 15% |
| Rev 3 **Gate 3** | "Performance budget measured across three configurations" |

**D4.2 is the sharp one.** Those thresholds are numbers specifically so that
rollback is not a judgement call during a production rollout. Without a baseline
they become a judgement call, on the estate's least familiar services.

## Scope

Three representative services. **Which three is an output of Run 0, not an
input.** "Highest-throughput HTTP API" cannot be identified before throughput is
measured; until then the candidates come from the register plus the estate's own
belief about which services matter, and Run 0 either confirms that belief or
corrects it. Record which happened — a wrong belief about where the load sits is
a finding in its own right.

| # | Service | Runtime | Why |
|---|---|---|---|
| 1 | *(highest-throughput HTTP API)* | .NET 10 | Most load-sensitive |
| 2 | *(NATS consumer with deepest processing)* | .NET 10 | Async path, invisible to HTTP monitoring |
| 3 | *(one ASP.NET MVC application)* | .NET Framework 4.8 | Highest-risk estate, per Rev 3 **F-I5** |

The 4.8 baseline is taken **now**, despite
[ADR-0012](../adr/0012-net10-first-sequencing.md) deferring 4.8 work to Phase 2.
Measurement is not instrumentation: no code, no recompile, no deploy. What is
deferred is instrumentation, not observation.

## Method

Three runs. **Run 0 first**, then Runs 1 and 2, which are re-run identically at
**D3.3**.

### Run 0 — passive production observation

Confirmed 2026-08-10: **no per-service throughput figures exist.** Run 0 is
therefore not a sanity check on the synthetic load — it is where the rate comes
from, and it is also a genuine uninstrumented baseline under real traffic rather
than under a script's idea of traffic.

**Window: one full business week, agreed in advance.** Fixed beforehand or it
becomes "whenever we looked," and the peak day is an estate fact nobody currently
knows. Record any week that is atypical — month-end, a campaign, an outage — and
if the week turns out to be atypical, say so rather than quietly reusing it.

Sources that exist today, with no metrics stack:

| Signal | Where it comes from now |
|---|---|
| HTTP request rate, per route, per hour | IIS logs via `LogParser`; Kestrel access logs for .NET 10 |
| HTTP latency | IIS `time-taken` field. Server-side only — excludes network |
| CPU, working set, allocation rate | Windows performance counters, sampled at a fixed interval |
| NATS message rate per subject and consumer | NATS server monitoring endpoint |
| CouchDB call volume | CouchDB `_stats`, and its HTTP access log by user agent |
| GC behaviour | .NET CLR Memory counters |

Two limits, both worth recording rather than working around. IIS `time-taken`
measures the server, so a client-side latency problem is invisible here. And
these sources cover services individually — nothing correlates them, which is the
project's entire premise, so Run 0 is also the clearest available demonstration
of the problem being solved.

### Run 1 — constant rate at production throughput measured in Run 0 Gives the p50, p95,
and p99 comparison in the regime actually operated.

### Run 2 — ramp to saturation

Gives the throughput ceiling with and without
telemetry, which is what D4.2's headroom thresholds are really about.

Both are needed because instrumentation overhead is not linear in throughput. A
constant mid-range load can sit entirely below the point where exporter queues
and batch flushes begin competing for CPU — measuring a regime you never operate
in.

Runs 1 and 2 are compared back against Run 0. If synthetic load at the measured
rate does not reproduce Run 0's CPU and latency, the script is wrong and the
D3.3 comparison built on it would be wrong in the same direction.

🔒 Traffic replay was rejected. A recorded KYC corpus is regulated data at rest
in a test environment — a larger problem than the measurement it would serve.

## Metrics captured

Per run, per service: p50 / p95 / p99 latency · CPU · working set · allocation
rate · throughput.

## Run conditions — record these or the comparison is not like-for-like

- Hardware and host specification
- .NET runtime version
- For IIS: application pool recycle configuration and idle timeout. Rev 3
  **F-I3** notes both affect working set and throughput readings
- Anything else running concurrently on the host
- Exact version of the load script

## Worksheet

**Service:** ___________  **Runtime:** ___________  **Date:** ___________
**Run conditions:** ___________

| Run | p50 | p95 | p99 | CPU | Working set | Alloc rate | Throughput |
|---|---|---|---|---|---|---|---|
| 0 — passive production | | | | | | | |
| 1 — constant @ measured rate | | | | | | | |
| 2 — ramp to saturation | | | | | | | |

**Observation window:** ___________ to ___________
**Window atypical?** ___________
**Peak rate measured, and when it occurred:** ___________
**Median rate measured:** ___________
**Rate used for Run 1, if not the peak, and why:** ___________
**Does Run 1 reproduce Run 0's CPU and latency?** ___________

## At D3.3

The same script, unchanged, against three configurations: telemetry off,
telemetry on, telemetry on with production sampling. Compare to the rows above.

Rev 3: *~5% is a tripwire, not a promise.* Materially above it means find the bug
— usually a span created per loop iteration — not accept the number and move on.

At Phase 3, where instrumented and uninstrumented instances can run behind one
load balancer, that A/B comparison supersedes this synthetic delta: same
hardware, same traffic, same hour.
