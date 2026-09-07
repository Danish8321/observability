Status: closed — 2026-09-07

# The trace pipeline is maintained twice, and the net48 copy is the one nobody runs

`RaksawiObservabilityExtensions.net10.cs:48-79` and
`RaksawiObservability.net48.cs:48-70` build the same pipeline independently:
resource, `ParentBasedSampler(TraceIdRatioBasedSampler(...))`, the NATS source,
`options.ActivitySources`, `AddHttpClientInstrumentation`, the
`AllowlistProcessor`, and the OTLP exporter. `ConfigureOtlp` is duplicated
verbatim — 15 lines including its comment — at `net10.cs:90-101` and
`net48.cs:75-87`.

Only two things genuinely differ: `AddAspNetCoreInstrumentation()` vs
`AddAspNetInstrumentation()`, and the CouchDB enrichment (net10 only, see
below).

## Impact

Every governance change to the trace pipeline has to be applied twice by hand,
with nothing checking the second application. ADR-0022 defers 4.8 validation
past the demo, so the copy that will silently fall behind is the one with no
test coverage and no live run behind it. Issue 04 in `.scratch/demo-readiness`
is precedent: the same OTLP path bug had to be fixed in both files, and was
only found because the net10 side was running.

## Two asymmetries that may be bugs rather than intent

1. **CouchDB URL redaction is net10-only.** Raised separately as issue 11 —
   it is a redaction gap, not just a duplication symptom, but it was found by
   diffing these two copies and is the clearest evidence of the cost.
2. **ASP.NET metrics are net10-only.** `net10.cs:82` has
   `AddAspNetCoreInstrumentation()` in `WithMetrics`; `net48.cs:65-69` has no
   ASP.NET instrumentation on the meter provider. May be a limitation of
   `OpenTelemetry.Instrumentation.AspNet 1.12.0-beta.1`; if so, say so in a
   comment.

Asymmetry 1 is a redaction gap and should be split into its own ticket if
confirmed — filed here because it was found while comparing the two copies.

## Fix

Extract the shared configuration once. Both sides hold a `TracerProviderBuilder`
(`WithTracing`'s lambda parameter and `Sdk.CreateTracerProviderBuilder()`), so a
single extension compiles for both target frameworks:

```csharp
internal static TracerProviderBuilder AddRaksawiCore(
    this TracerProviderBuilder builder, RaksawiObservabilityOptions options) => ...
```

Same for `MeterProviderBuilder` and for `ConfigureOtlp`. Each entry point is
then left with its runtime-specific instrumentation call and its own lifecycle
(host builder vs `IDisposable`), which is the only part that genuinely differs.

This is the same argument `AllowlistRules` already makes in its own doc — one
table, two readers, because "if these rules existed twice, build-time and
run-time enforcement could disagree, and the disagreement would be silent in
exactly the direction that matters".

## Fixed (2026-09-07)

New `src/Raksawi.Observability/RaksawiPipeline.cs`, compiled for both targets.
Four extension methods and two helpers, all shared:

- `AddRaksawiIdentity(TracerProviderBuilder, …)` — resource, sampler, NATS
  source, the service's sources.
- `AddRaksawiExport(TracerProviderBuilder, …)` — allowlist processor, then the
  OTLP trace exporter.
- `AddRaksawiIdentity(MeterProviderBuilder, …)` — resource, the library meter,
  the service's meters.
- `AddRaksawiExport(MeterProviderBuilder, …)` — the OTLP metric exporter.
- `TryRedactCouchDbUrl` and `ShouldTrace` — the two ADR-0023 rules, so both
  runtimes apply the same policy through their different instrumentation hooks.
- `ConfigureOtlp` — was duplicated verbatim, comment included.

**Split into identity + export rather than one method** because the
runtime-specific instrumentation has to go *between* them: the allowlist
processor must be the last thing before the exporter (ADR-0003), and that
ordering is the constraint most worth keeping visible at each call site. Each
entry point now reads: identity → its own instrumentation → export.

Both entry points lost their `using OpenTelemetry.Exporter;` and their private
`ConfigureOtlp`.

## The two asymmetries this ticket listed

1. **CouchDB redaction missing on 4.8** — filed as issue 11, now wired. The
   netfx hooks turned out to exist: `FilterHttpWebRequest` and
   `EnrichWithHttpWebRequest` (HttpClient is instrumented at `HttpWebRequest` on
   .NET Framework, so the `HttpRequestMessage` callbacks never fire there).
   Confirmed by compiling against 1.17.0. Issue 11 stays open pending a real
   4.8 span.
2. **ASP.NET metrics** — left as-is. The 4.8 meter provider still has no ASP.NET
   instrumentation, and whether `OpenTelemetry.Instrumentation.AspNet
   1.12.0-beta.1` offers a metrics equivalent is a Phase 2 question, not a
   refactor question. Recorded here rather than silently "fixed" by adding a
   call that may not exist.

## Verified

`check.sh` clean — both `net48` and `net10.0` build with zero warnings, which is
the check that matters here since the 4.8 path has no test coverage.

New `tests/Raksawi.Observability.Tests/RaksawiPipelineTests.cs`, five tests over
the now-shared CouchDB rules: configured host redacts, unconfigured host fails
open, redaction can be disabled, the changes feed is not traced, a null URI is
traced rather than dropped. `test-fast.sh` 148/148, up from 143.

One build error during the refactor, fixed: `CS0419` ambiguous `cref` on the
overloaded `AddRaksawiIdentity` / `AddRaksawiExport` in the class remarks.

## Comments
