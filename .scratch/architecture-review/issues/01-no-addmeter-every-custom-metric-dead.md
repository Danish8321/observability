Status: closed — 2026-09-07

# No `AddMeter` anywhere — every custom metric is collected by nothing

`grep -rn "AddMeter" --include=*.cs src samples tests` returns zero hits.

The OpenTelemetry .NET `MeterProvider` collects only meters it has been
subscribed to via `AddMeter(name)`. Neither metrics pipeline subscribes any:

- `src/Raksawi.Observability/RaksawiObservabilityExtensions.net10.cs:80-85`
  (`WithMetrics`) registers AspNetCore, HttpClient and Runtime instrumentation
  plus the OTLP exporter, and nothing else.
- `src/Raksawi.Observability/RaksawiObservability.net48.cs:65-69` is the same,
  minus AspNet.

`RaksawiObservabilityOptions` has an `ActivitySources` list
(`RaksawiObservabilityOptions.cs:67`) and no `Meters` counterpart, so a
consumer has no way to register one either.

## Impact

Two live consequences, both silent:

1. **The allowlist's own dropped-key counter exports nothing.**
   `AllowlistProcessor.cs:22-30` creates `Meter("Raksawi.Observability.Allowlist")`
   and the counter `raksawi.telemetry.attributes.dropped`. Its XML doc argues
   the counter is what makes a silent failure "answerable from a dashboard
   rather than by reading library source" (ADR-0003). That is false today —
   the counter increments in-process and is never collected, so the dashboard
   has no data and the failure stays exactly as silent as the doc says it
   must not be.

2. **The reference service's metrics export nothing.**
   `samples/Screening.Domain/ScreeningTelemetry.cs:27-74` declares
   `Meter("Raksawi.Screening")` with `screening.applications.screened`,
   `screening.duration` and `screening.applications.abandoned`. None reach a
   store. `samples/README.md` presents these as the patterns the estate should
   copy, so the pattern being copied does not work.

This is the same failure the sample warns about for traces — "a source that is
not registered emits nothing, silently, which is the second most common wiring
mistake" (`ScreeningTelemetry.cs:14-16`) — reproduced on the metrics side, in
the library itself.

## Why no gate caught it

- `e2e.sh` POSTs synthetic OTLP payloads directly at the collector
  (`.claude/scripts/e2e.sh:103`) and never runs an instrumented process, so it
  exercises the collector enforcement point only. The script says so honestly
  in its header.
- `tests/Raksawi.Observability.Tests/AllowlistProcessorTests.cs` asserts which
  tags survive `OnEnd`; it contains no `MeterListener` and never observes the
  counter.

## Fix

Add the symmetric option:

```csharp
// RaksawiObservabilityOptions.cs, next to ActivitySources
/// <summary>Meter names belonging to the application itself.</summary>
public IList<string> Meters { get; } = new List<string>();
```

Register in both metric pipelines:

```csharp
.AddMeter(AllowlistProcessor.MeterName)
.AddMeter(options.Meters.ToArray())
```

`AllowlistProcessor.MeterName` is already `internal const`, so the library can
subscribe its own meter without widening any surface.

Then `o.Meters.Add(ScreeningTelemetry.MeterName)` in both
`samples/Screening.Api/Program.cs` and `samples/Screening.Worker/Program.cs`.

## Verification required

A `MeterListener`-based unit test asserting `raksawi.telemetry.attributes.dropped`
is observed after a drop — mirroring how `ScreeningTelemetryTests.cs:20` already
listens by meter name. "It compiles" does not distinguish this bug from its fix.

Do issue 02 in the same change: subscribing the meter is what makes 02's
unbounded dimension actually reach a store.

## Fixed (2026-09-07)

`RaksawiObservabilityOptions.Meters` added, symmetric with `ActivitySources`.
Both metric pipelines now subscribe the library's own meter and the service's:

```csharp
.AddMeter(AllowlistProcessor.MeterName)
.AddMeter(options.Meters.ToArray())
```

Applied in `RaksawiObservabilityExtensions.net10.cs:82-87` and
`RaksawiObservability.net48.cs:67-71`. `samples/Screening.Api/Program.cs` and
`samples/Screening.Worker/Program.cs` both register
`ScreeningTelemetry.MeterName`.

## Verified

`tests/Raksawi.Observability.Tests/AllowlistMetricsTests.cs`, four tests. The
pair that pins the actual defect drives a real `MeterProvider` through a
collecting `BaseExporter<Metric>`:

- `The_counter_reaches_a_collector_when_the_meter_is_registered` — with
  `AddMeter(AllowlistProcessor.MeterName)`, `raksawi.telemetry.attributes.dropped`
  is exported.
- `The_counter_reaches_nothing_when_the_meter_is_not_registered` — without it,
  nothing is. This is the bug reproduced as a test, so a regression fails rather
  than going quiet.

`check.sh` clean (build all TFMs + format). `test-fast.sh` 139/139, up from 135.

Not covered: that `AddRaksawiObservability` itself performs the registration —
that needs a host builder and belongs with an integration fixture. The
positive/negative pair above proves the mechanism; the call sites were changed
together and are two lines each.

## Comments
