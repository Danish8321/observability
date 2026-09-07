# 28. Log records are allowlisted in-process on .NET 10, and at the collector only on 4.8

Date: 2026-09-07

## Status

Accepted. Completes [ADR-0003](./0003-runtime-allowlist-at-source.md) for the
log signal and makes [ADR-0004](./0004-free-text-telemetry-and-exceptions.md)
load-bearing. Deviates from Rev 3 **I3.2** for the .NET Framework 4.8 target.

## Context

ADR-0003 states three enforcement points with default deny at each: analyzer at
build, library before export, collector before storage. For log records the
middle one did not exist. `AddRaksawiObservability()` wired a tracer provider
and a meter provider and no logger provider at all, so a service adopting the
package got no OTLP logs — and `DataClass.cs` said Class 2 is "permitted on
spans and logs" with nothing behind the second half.

That is worse than it sounds, because a structured log property **is** an
attribute. `LogInformation("Screened {ApplicationId}", id)` produces an
attribute keyed `ApplicationId` on the exported record, in the same shape a span
attribute takes. Every argument for filtering span attributes at the source
applies to it unchanged; none of them were being applied.

The gap was not recorded anywhere as a decision, which is the one failure mode
this repository is meant not to have.

## Decision

**Logs are wired, and filtered in-process, on .NET 10.**
`AddRaksawiObservability()` adds `.WithLogging(...)` with the shared resource,
an OTLP exporter on `v1/logs`, and `LogAllowlistProcessor` — a
`BaseProcessor<LogRecord>` that drops every attribute whose key is not
allowlisted, using the same `AttributeAllowlist` instance shape the span
processor uses.

**The log allowlist is the span allowlist**, families and carve-outs alike,
because Rev 3 D2.1 permits Class 2 on spans *and* logs. One exception: the
conditional CouchDB pair (`url.full`, `url.query`) is **unconditionally denied**
on a log record. Those keys survive on a span to a known CouchDB host only
because `CouchDbUrlPolicy` rewrote the document identifier first; nothing
rewrites a URL that a service wrote into a log, so the exemption has no
precondition and the keys are dropped. The collector states the same rule in its
own `log_statements` block.

**On .NET Framework 4.8 the collector is the sole log control.** This is an
explicit deviation, not an oversight — see Consequences.

**Log property names are attribute keys and must be named as such.** The
reference service now writes `"Screened {application.id} with outcome {Outcome}"`
rather than `{ApplicationId}`.

## Consequences

- 🔒 **The message body is not filtered and cannot be.** The processor drops
  attributes by key; a template that interpolated a value into the text has
  already destroyed the structure any filter needs. This is exactly why ADR-0004
  bans interpolated log strings, and this processor is what turns that ban from
  a style rule into the thing standing between a Class 3 value and the store.
  The collector's free-text scan remains the only net under a body.
- 🔒 **A PascalCase property is silently dropped.** `{ApplicationId}` matches no
  family and no declaration, so the conventional .NET naming produces a log line
  whose property is simply gone. That is default deny working as designed, and
  it is a real adoption cost: services must name log properties in the estate
  vocabulary. `e2e-instrumented.sh` asserts both halves on one record —
  `application.id` survives, an undeclared `Outcome` does not.
- **The dropped-key metric now carries a `telemetry.signal` dimension**
  (`span` / `log`), so a service losing log properties is visible rather than
  mysterious. The 100-distinct-key cap from ADR-0002's cardinality fix is
  per-signal, not shared.
- **4.8 deviation.** OpenTelemetry .NET 1.17.0 exposes no
  `Sdk.CreateLoggerProviderBuilder`: a logger provider on 4.8 requires taking a
  dependency on `Microsoft.Extensions.Logging`, and the 4.8 services in this
  estate are not assumed to use it. Rather than force a logging abstraction on
  them to gain a filter, 4.8 keeps the ADR-0009 posture — collector only,
  fail-closed — which is the same bargain already accepted for agent-instrumented
  processes. Revisit in Phase 2 alongside ADR-0005, when a real 4.8 service is in
  front of us and the question of what it logs with has an answer.
- **Two enforcement points for logs on .NET 10, one on 4.8.** The README states
  this rather than claiming three everywhere.
- The rule is now stated in three places for logs — the library, the collector's
  `log_statements`, and the tests that compare them.
  `CollectorAllowlistContractTests` fails if the span keep and the log keep
  drift, and asserts every signal's pipeline passes through
  `transform/allowlist`.
- **No analyzer rule for logs, and ADR-0004's is still owed.** RKS001 flags an
  undeclared *span* attribute key; nothing flags an undeclared log property, so
  a service learns about the drop from the dropped-key metric rather than at
  build. ADR-0004's interpolation rule is likewise still unbuilt — there is no
  RKS004 and `CA2254` is not enabled. Both are now more valuable than they were
  yesterday, because in-process log filtering is what makes an interpolated body
  the one unfiltered path left. Recorded here rather than assumed.
