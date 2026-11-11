# 39. Traces are sampled at the collector, after the trace is complete

Date: 2026-11-10

## Status

Accepted — plan D4. Amends [ADR-0010](./0010-sampling-defaults.md): services
still must set a sampler explicitly, and the value they set is now 1.0.

## Context

Until now every trace was stored. That is affordable for a demo and not for
production, and the obvious fix, lowering each service's head sampling ratio,
throws away failures at the same rate as successes: at 0.1 nine abandoned
applications in ten have no trace. Rev 3 **I3.5** already says so, and
dashboard panel 2.6 ("error traces, kept at 100%") was marked unbuilt for that
reason.

A decision about whether a trace is worth keeping can only be made once the
trace has finished, and the only place that sees a whole trace is the collector.

## Decision

- **Services head-sample at 1.0** and the collector decides. A service that
  lowers its ratio takes the decision away from the collector, so 1.0 is the
  value ADR-0010's "must be set explicitly" now expects.
- **`tail_sampling` in the traces pipeline** of `deploy/collector/config.yaml`
  keeps a trace if any policy matches:

  | Policy | Keeps |
  |---|---|
  | `errors` | any span with status `ERROR`, except an HTTP client span that got a 404 |
  | `slow` | traces longer than 2 s |
  | `abandoned` | any span with `screening.abandoned = true` |
  | `base-rate` | `TRACES_BASE_SAMPLE_PERCENT` percent of the rest, default 10 |

  The 404 exception was found by running it: the HTTP client instrumentation
  marks every 4xx an error, screening-api reads CouchDB before each create and
  gets a 404, so a plain status policy stored all 20 of 20 healthy traces. A
  404 that matters turns the calling span red, which still matches.
  `abandoned` is its own policy because a `not_found` abandonment leaves the
  span's status unset (samples/README.md, "not found is not an error").
- **After the allowlist, before the batch.** A policy may read only keys that
  survived `transform/allowlist`, so sampling cannot become a reason to let an
  undeclared key through. The collector's keeps therefore now admit the policy
  pack's declared Class 0 and 1 keys, which they had been dropping; a contract
  test holds every declared key in both keeps.
- **The decision waits 30 s** from a trace's first span, holding at most 50,000
  traces in memory.

## Consequences

- **One gateway instance.** Every span of a trace must reach the same
  collector. Two replicas behind a plain load balancer would each see part of a
  trace and decide separately. Scaling out needs a `loadbalancing` exporter
  tier routing by trace ID in front; until then this hardens R2's "one replica
  for now" from a recommendation into a requirement.
- **A span arriving more than 30 s after its trace began** gets the decision
  already made. If that was "drop", a late error is lost. The reference flow
  finishes in under a second; a workflow that waits on a person does not fit in
  one trace anyway (`correlation.id` is what spans those).
- **30 s, 50,000 and 10% are not sized** from real traffic. They are Infra's to
  tune from `otelcol_processor_tail_sampling_*` once there is some
  (HANDOFF.md).
- Traces now reach the store about 30 s later than logs and metrics.
- "Accepted" minus "sent" spans is no longer a loss signal on its own: sampled
  out traces are in that gap by design.
- Metrics and logs are not sampled. Counters stay exact.
- `e2e.sh` and `e2e-instrumented.sh` set the base rate to 100 because they
  assert on healthy traces; `e2e-demo.sh` runs the default and asserts that
  every abandoned trace is stored while healthy ones are not all stored.
