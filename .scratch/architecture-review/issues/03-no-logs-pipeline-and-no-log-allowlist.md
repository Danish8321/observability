Status: open

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

## Comments
