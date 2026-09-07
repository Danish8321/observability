Status: closed — 2026-09-07

# The W3C trace-context warning is computed on both runtimes and observable on neither

`ServiceIdentity.EnsureW3CTraceContext()` (`ServiceIdentity.cs:22-35`) returns a
non-null warning string when the id format had to be corrected. Per ADR-0005 it
warns and never throws. Both callers then lose the warning.

**.NET 10** — `RaksawiObservabilityExtensions.net10.cs:38-42`:

```csharp
var w3cWarning = ServiceIdentity.EnsureW3CTraceContext();
if (w3cWarning is not null)
{
    builder.Logging.AddFilter("Raksawi.Observability", LogLevel.Warning);
}
```

This raises the minimum log level for a category and then logs nothing. The
string is never written anywhere. There is also no logger available at this
point — `builder.Build()` has not run — which is presumably how it ended up as
a filter call.

**.NET Framework 4.8** — `RaksawiObservability.net48.cs:72, 88-105`: the warning
is stored on `Handle.W3CWarning`, but `Handle` is a `private sealed` nested
class and `Start` returns `IDisposable`. No caller can reach the property. Its
own doc says "surfaced rather than logged, because there is no logging
abstraction to assume on this runtime" — it is neither surfaced nor logged.

## Impact

On 4.8 this is the diagnostic for the failure the file itself calls "the single
most important line in the 4.8 path" (`RaksawiObservability.net48.cs:41-43`):
`Hierarchical` id format silently splits a trace in two rather than erroring.
The warning also carries the important distinction — if it appears *after* an
`Activity` has been created, traces have already split — and nobody can read it.

## Fix

- **net48**: return a public handle type exposing the warning, e.g.
  `public interface IRaksawiObservabilityHandle : IDisposable { string? W3CWarning { get; } }`,
  so `Application_Start` can write it to whatever the host uses. Additionally
  or alternatively write it to `System.Diagnostics.Trace` / an `EventSource`,
  which needs no abstraction to assume.
- **net10**: log it once the host is built, or write it via the same
  `EventSource`. Delete the `AddFilter` call — it changes behaviour for
  unrelated categories and does not do what its position implies.

Either way, delete whichever path is replaced (no dead property left behind).

## Verification required

Test that forces `Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical`
before the call and asserts the warning is observable through the public
surface.

## Fixed (2026-09-07)

**Shared.** `ServiceIdentity.W3CCorrectedMessage` is now a named constant rather
than a string built inline, so both runtimes report the same text for the same
condition and the .NET 10 side can log it as a constant template (ADR-0004 bans
interpolated log messages).

**.NET 10.** The `AddFilter` call is deleted — it raised a level for an
unrelated category and wrote nothing. When the correction happens,
`AddRaksawiObservability` now registers `W3CTraceContextWarning`, a small
`IHostedService` that logs the warning once at start. Its presence in the
service collection *is* the condition, so there is no flag to keep in sync. A
hosted service rather than a direct log call because
`AddRaksawiObservability` runs before the host is built and has no logger.

**.NET Framework 4.8.** The private nested `Handle` is now the public
`RaksawiObservabilityHandle`, and `Start` returns it instead of `IDisposable`.
`W3CWarning` is reachable. Widening a return type is source-compatible, so
existing `IDisposable _o = Start(...)` call sites are unaffected.

## Docs corrected alongside

`docs/onboarding/integration.md` already instructed readers to "Check
`Handle.W3CWarning`" — against a private nested class returned as
`IDisposable`, which no caller could do. Same doc-vs-code contradiction as
issue 04, on the 4.8 path this time. Updated to `_observability.W3CWarning`,
with the field typed as the handle. `README.md` 4.8 paragraph updated to name
the handle and say what a non-null warning means.

## Verified

`tests/Raksawi.Observability.Tests/W3CTraceContextTests.cs`, three tests:

- `Correcting_the_format_produces_a_warning` — forces
  `ActivityIdFormat.Hierarchical` (what 4.8 starts as), asserts the message
  comes back and the format is corrected and forced. Restores global state in a
  `finally`.
- `A_format_that_was_already_correct_produces_no_warning`.
- `The_warning_is_written_at_host_start` — drives `W3CTraceContextWarning`
  through a capturing `ILogger` and asserts one `Warning` carrying exactly
  `ServiceIdentity.W3CCorrectedMessage`. This is the regression: the old path
  computed the warning and wrote it nowhere.

`ServiceIdentity` had no test coverage at all before this.

`check.sh` clean (net48 and net10 both build). `test-fast.sh` 143/143, up from
140.

Not covered: that `AddRaksawiObservability` registers the hosted service — same
host-builder gap noted on issue 01.

## Comments
