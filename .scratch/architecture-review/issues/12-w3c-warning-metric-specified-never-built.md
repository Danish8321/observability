Status: blocked — metric landed and verified on .NET 10 2026-09-08. 4.8 deferred
out of the build the same day (ADR-0029), so the value-1 case cannot be reached
until that decision is reversed.

# ADR-0005's W3C warning metric is specified but does not exist

[ADR-0005](../../../docs/adr/0005-enforcing-the-framework-wiring.md) specifies a
runtime check at `Start()`:

> **Run time**, at `Start()`: inspect `Activity.DefaultIdFormat`. If it is not
> W3C, emit a loud warning **and increment a metric**. Do **not** throw.

and states the reason the metric is not optional:

> The warning is paired with a metric because a warning in a log on an IIS host
> is not a control. The metric surfaces on the stack health dashboard (I3.9),
> next to coverage.

**The warning exists. The metric does not.**

- `ServiceIdentity.EnsureW3CTraceContext()` returns the corrected-message
  string (`ServiceIdentity.cs:23-30`).
- .NET 10 surfaces it through `W3CTraceContextWarning` as an `ILogger` warning
  (`W3CTraceContextWarning.net10.cs:31`).
- 4.8 surfaces it as `RaksawiObservabilityHandle.W3CWarning`, for the host to
  write "wherever this application already writes startup diagnostics"
  (`RaksawiObservability.net48.cs:128`, and Part B of
  `docs/onboarding/integrating-a-service.md`).

No counter is created anywhere in `src/`. The only metric the mechanism layer
emits is `raksawi.telemetry.attributes.dropped`
(`AllowlistDropMetric.cs:60`).

## Impact

Panel 4.10 of `docs/diagnostic-queries.md` — "W3C format warnings, by service"
— has no data source, so it cannot be built. That panel is the one whose stated
purpose is that the log line is *not* the control.

The gap lands hardest exactly where ADR-0005 argues the risk is highest. On
4.8 the library does not even write the warning itself; it hands the string to
the host and depends on the host writing it somewhere a human later reads.
ADR-0005 calls that class of dependency "the weakest available control at
exactly the point where the consequences are least visible", and it is
currently the only control there is for a failure mode (F-D2) whose symptom is
every cross-runtime trace silently splitting in two.

Bounded today by the same deferrals as issue 11: no 4.8 consumer exists before
Phase 2 (ADR-0012, ADR-0022). It must be closed before one does.

## Fix

A counter in the mechanism layer, incremented on the non-null return from
`EnsureW3CTraceContext()`, dimensioned so 4.10 can group by service. The
resource already carries `service.name`, so no new dimension is needed and no
new allowlist key is required — which is the reason to prefer a bare counter
over one dimensioned by hand.

Both entry points must increment it: net10 at the point that registers
`W3CTraceContextWarning`, net48 inside `Start()`. Wiring it on one runtime only
would leave the gap on the runtime the ADR is about.

The counter must be registered on the meter provider the library builds, or it
increments in-process and reaches nothing — the exact failure issue 01 records
for the sample's own metrics, and the reason `e2e-instrumented.sh` exists.

## Verification required

A unit test proving the counter increments is necessary and not sufficient, for
the reason above. Closing this needs the counter observed **at the sink**, the
way `e2e-instrumented.sh` asserts on the sample's counter. That is awkward
because the .NET 10 path corrects the format before any `Activity` exists, so
the warning path does not fire naturally on that runtime — the fixture has to
set `Activity.DefaultIdFormat` to `Hierarchical` first.

That makes this cheapest to verify alongside the Phase 2 4.8 fixture (issue
11), where the failure is the runtime default rather than something a test has
to arrange.

## Code landed (2026-09-08)

`TraceContextMetric.cs` — an observable gauge, not the counter this ticket
proposed. Writing the fix surfaced why: a counter has to be incremented inside
`EnsureW3CTraceContext()`, which both entry points call **before** the meter
provider is built, so the measurement would be taken with nothing subscribed
and reach no store. That is the failure this ticket is about, reproduced inside
its own fix. A gauge is read at collection time, so the ordering cannot break
it, and the state holds for the process lifetime rather than only the export
window containing startup. Recorded in ADR-0005 rather than left here, since
the ADR's "increment a metric" is what a reader builds a panel against.

Recorded inside `ServiceIdentity.EnsureW3CTraceContext()` rather than at each
entry point, for the reason the CouchDB policy moved into `RaksawiPipeline`
(issue 06): both runtimes call it, so the two cannot drift. Registered on the
provider in `RaksawiPipeline.AddRaksawiIdentity`.

Healthy processes report 0 rather than nothing. "The check ran and the format
was fine" and "nothing is reporting" are different answers, and panel 4.1 is
the one that answers the second.

## .NET 10 half verified (2026-09-08)

`test-fast.sh` — 181 passed, including one that builds a real `MeterProvider`
through `AddRaksawiIdentity` and reads the gauge back off an exporter. A test
asserting only `TraceContextMetric.Current` would have passed against an
unregistered meter, which is the whole failure mode.

`e2e-instrumented.sh` — sixteen assertions, including:

```
  ok      present the trace-context gauge
```

on telemetry that left a real service and crossed both enforcement points. That
discharges the "observed at the sink" requirement below, for .NET 10.

## Still open, deliberately

The value is **0** in that run. .NET 10 starts W3C, so the assertion proves the
series is registered and exported — not that the correction path produces 1
where it matters. The value-1 case is the .NET Framework default, and this
ticket is about the runtime where ADR-0005 says silent failure is most likely
to survive to production.

Closes when a real 4.8 start emits the gauge at 1, which is the same Phase 2
fixture issue 11 waits on (ADR-0005, deferred by ADR-0022). Verifying one
runtime and inferring the other is what issue 11 exists to refuse.

**Blocked by ADR-0029 (2026-09-08).** 4.8 left the build, so there is no runtime
on which the value can be 1 and nothing to verify against. The metric itself is
unaffected: it is recorded in `ServiceIdentity.EnsureW3CTraceContext()`, which
is shared compilation, so a restored 4.8 target gets it without further wiring.
That is the one part of this ticket the deferral does not put at risk.

## Comments
