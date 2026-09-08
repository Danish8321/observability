Status: open — found 2026-09-08 while building the dashboard guide

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

## Comments
