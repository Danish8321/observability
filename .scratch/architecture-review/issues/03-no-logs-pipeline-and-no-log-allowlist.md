Status: closed — 2026-09-07

# No logs pipeline exists, and ADR-0003's in-process control covers spans only

Neither entry point wires OpenTelemetry logging. `grep -rn "WithLogging|OpenTelemetryLoggerOptions|AddOpenTelemetry" --include=*.cs src samples`
returns one hit — `RaksawiObservabilityExtensions.net10.cs:47`, which is
`Services.AddOpenTelemetry()` for the tracer/meter builders. There is no
`WithLogging`, no OTLP log exporter, and no `logging.AddOpenTelemetry(...)`.

Separately, `AllowlistProcessor` is `BaseProcessor<Activity>`
(`AllowlistProcessor.cs:14`). There is no `BaseProcessor<LogRecord>` sibling.

## Impact

Two distinct problems.

**1. ADR-0003's second enforcement point does not exist for logs.** The stated
design is "three enforcement points, default deny at each: analyzer at build,
library before export, collector before storage". For log records the middle
one is absent, so the collector is the only control. `DataClass.cs:26` states
Class 2 is "permitted on spans and logs" — the "and logs" half has no
in-process governance behind it. Anything a service puts in a log scope or
message template reaches the collector unfiltered.

**2. README overstates what the one call does.** `README.md:41`:

> Traces, metrics, logs, resource attributes, redaction, sampler defaults, and
> exporter safety all follow.

Logs do not follow. A service adopting the package on the strength of that
sentence gets no log export at all and will not find out until an incident.

There is no ADR recording a decision to defer logs — the 27 ADRs cover
sampling, identity, allowlist composition, resource attributes, the 4.8 agent
path, but not this. So it currently reads as an omission rather than a
sequencing choice, which is the one thing `CLAUDE.md` says must not happen
("Nothing deviates silently").

## Fix

Decide, then make the repo say the same thing in both places. Either:

- **Wire it.** `builder.Logging.AddOpenTelemetry(o => o.SetResourceBuilder(...).AddProcessor(new LogRecordAllowlistProcessor(...)).AddOtlpExporter(... "v1/logs"))`,
  with an allowlist processor over `LogRecord.Attributes`. Note the net48 path
  has no `ILoggingBuilder` — `RaksawiObservability.Start` would need an
  `OpenTelemetryLoggerProvider` held by the returned handle, which interacts
  with issue 05.
- **Defer it explicitly.** New ADR stating logs are deferred to a named phase,
  that the collector is the sole log control until then, and correct
  `README.md:41` to say "traces and metrics".

Deferring is defensible; leaving README claiming otherwise is not.

## Fixed (2026-09-07)

Wired, not deferred — recorded as
[ADR-0028](../../../docs/adr/0028-logs-are-enforced-in-process-on-net10-only.md).

**Library.** `LogAllowlistProcessor : BaseProcessor<LogRecord>` drops any
attribute whose key is not allowlisted, using the same `AttributeAllowlist` the
span processor uses and evaluating with `isCouchDbSpan: false` — the conditional
`url.full`/`url.query` pair is unconditionally denied on a log, because nothing
redacts a URL a service wrote into one. It allocates only on the first drop.
`RaksawiPipeline.AddRaksawiLogging` adds it alongside the shared resource and an
OTLP exporter on `v1/logs`; `AddRaksawiObservability()` calls it through
`.WithLogging(...)`.

The dropped-key counter moved to `AllowlistDropMetric` so spans and logs share
the instrument and the ADR-0002 cardinality cap, and gained a
`telemetry.signal` dimension (`span` / `log`) — otherwise a service losing log
properties could not tell which signal it was losing them from.

**Collector.** New `log_statements` block plus a `logs:` pipeline through
`transform/allowlist`, so the rule holds for processes containing none of our
code.

**net48 is an explicit deviation, not an omission.** OpenTelemetry .NET 1.17.0
has no `Sdk.CreateLoggerProviderBuilder` — probed, `CS0117`. A logger provider
on 4.8 requires depending on `Microsoft.Extensions.Logging`, which the 4.8
services in this estate are not assumed to use, so 4.8 keeps the ADR-0009
posture: collector only, fail-closed. Revisited in Phase 2 with ADR-0005.

**The finding produced a governance consequence worth more than the fix.** A
structured log property *is* an attribute key, so the conventional
`{ApplicationId}` matches no family and no declaration and is dropped.
`ScreeningService` now writes `"Screened {application.id} with outcome
{Outcome}"` — one declared key, one undeclared — so the rule is demonstrated in
the reference implementation rather than only asserted.

README's overstated sentence is corrected and now says what is true on each
runtime; `docs/allowlist.md` gained an "on a log record" section; `DataClass`'s
"permitted on spans and logs" carries the naming rule that makes "and logs"
literal.

## Verified

- `check.sh` — 0 warnings, 0 errors, format clean.
- `test-fast.sh` — **178/178**, including 5 new `LogAllowlistProcessorTests`
  driven through a real `ILogger` and a real provider (a `BaseExporter<LogRecord>`
  that copies attributes at export, because `LogRecord` instances are pooled).
- `contract.sh` — 76/76 and `otelcol validate` accepts the new OTTL. The
  contract tests now compare the span keep against the log keep and fail on
  drift, assert the conditional pair is unconditional on a log, and assert every
  signal's pipeline passes through `transform/allowlist`.
- `e2e-instrumented.sh` — **18 assertions**, up from 15. On one real log record
  emitted by `screening-worker` and received by the sink: `application.id`
  present, `Outcome` absent. Scoped to the export request carrying that record,
  since `application.id` is legitimately present on a span in the same file.

## Comments
