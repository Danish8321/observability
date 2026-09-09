Status: open

# Nothing detects the stack being dead, and the self-scrape cannot

The collector now reports its own health (ADR-0031), which closes the gap where
`level: detailed` produced metrics that reached nothing. It does not close, and
structurally cannot close, the failure Rev 3 **I3.8/I3.9** names.

A dead collector stops scraping itself. The series stop. Absence of data is the
only signal, and absence is indistinguishable from a quiet period — a weekend, a
scaled-down environment, a service that legitimately went idle. Every panel on
dashboard 4 is built from data the collector forwards, so every one of them
degrades to "empty" in exactly the same way whether the estate is quiet or the
stack is gone.

The scrape targets one layer out are in better shape than they look, and this
was checked rather than assumed. `up` is synthesised per target — 1 reachable,
0 not — and it is **not** subject to `metric_relabel_configs`, so the
`keep gnatsd_*|jetstream_*` and `keep otelcol_*` in the two scrape jobs let it
through. Verified at the sink:

```
  ok      present the per-target scrape health series
```

So a dead `nats-exporter` (ADR-0030) does produce `up{job="nats"} 0`, and a
failing self-scrape does the same. That half is covered today. What is not
covered is the layer that synthesises those numbers.

## Impact

Panel 4.11, "dead man's switch, four states", is the specified answer and is
unbuilt (❌ in `docs/onboarding/backend-and-dashboards.md`). Until it exists the
whole observability stack is unmonitored, which is the exact circularity the
programme's own goal — reduced MTTR — cannot survive. An incident that begins
with the stack failing starts with no telemetry and no notification that there
is no telemetry.

Bounded today only by the demo boundary (ADR-0022): production KYC traffic does
not pass through this stack. It has to exist before that changes, and it is the
one item on the dashboard work list that cannot be deferred behind the others,
because it is what makes the others trustworthy.

## Fix

Not a panel. A panel cannot show its own absence, and building one that appears
to is worse than leaving it out.

Three parts, and none is in this repo yet:

1. **A heartbeat with an external observer.** Something outside the stack that
   expects a signal on a schedule and alerts on its absence — the classic dead
   man's switch. The observer must not be the collector, SigNoz, or anything
   that shares their failure domain, which is the whole design constraint.
2. **An alert on `up == 0`.** The series already arrives, so this is an alerting
   rule rather than a pipeline change, and it covers every scrape target — the
   NATS exporter today, anything added later for free. It does **not** cover the
   collector itself: a collector that is not running synthesises nothing, which
   is what part 1 is for.
3. **The four states named.** 4.11 specifies four; `docs/diagnostic-queries.md`
   should say which, since "healthy", "no data", "stack down" and "observer
   down" are not interchangeable and the fourth is what stops the observer
   itself becoming an unmonitored dependency.

Part 2 is the cheapest and is available now. Parts 1 and 3 need a decision about
where the observer lives, which is an infrastructure question this repo does not
currently own — and part 1 is the one that actually closes this ticket.

## Verification required

Kill things and watch. Stop the collector: the alert fires. Stop
`nats-exporter`: `up{job="nats"}` goes to 0 and the alert fires. Stop the
observer: something says so. An unverified dead man's switch is worse than
none — it is a control everyone believes in.

`e2e-instrumented.sh` currently asserts `up` arrives, which proves the series
survives the pipeline. It does not prove the 0 case, because every target in
that run is reachable. Closing part 2 means asserting `up` at 0 with a target
deliberately stopped.

## Comments

Ordering note: this ticket is what makes issues 05, 06 and 07 readable. Each of
those is a case where a panel shows a healthy-looking value while the thing it
describes is broken. They are the same class of failure at three scales, and a
working dead man's switch is the backstop under all three.

