# Phase 2 — validating the .NET Framework 4.8 failure modes

**Status:** not started. Phase 2, per
[ADR-0012](../adr/0012-net10-first-sequencing.md).
**Gates:** [ADR-0005](../adr/0005-enforcing-the-framework-wiring.md)'s analyzer
and startup rules are **not implemented until this validation runs**.

---

## Why this exists

The 4.8 implementation guide names three failure modes and states that all three
fail silently: telemetry arrives, dashboards populate, and traces are broken.

ADR-0005 specifies rules to catch two of them. Those rules were written from the
guide, not from observation. Implementing them first would encode unverified
behaviour into a control whose own failure would also be silent — a rule firing
on the wrong condition is indistinguishable from a rule not firing.

## Method

**Reproduce in a fixture, then confirm on one real service.**

A minimal ASP.NET MVC 4.8 fixture proves the failure modes and the rules. One
real legacy service then confirms the fixture was not lying about the
environment — real `Web.config`, real module ordering, real application pool.

The fixture is not throwaway: it becomes the 4.8 half of `e2e.sh` when 4.8 work
resumes.

## The three failures

### 1 — W3C trace context not forced

**Reproduce.** Omit the `Activity.DefaultIdFormat` / `ForceDefaultIdFormat` lines
from `Application_Start`. Call the fixture from a .NET 10 service.

**Expect.** The 4.8 hop starts a new trace. Two `trace_id` values where there
should be one. No error anywhere.

**Confirm.** Inspect the actual outbound header — a proprietary `Request-Id`
rather than `traceparent`.

**Then.** Add the lines, confirm one unbroken trace. Confirm the ADR-0005
analyzer rule errors on the version without them, and the startup check warns and
increments its metric when the format is wrong at runtime.

**Also test the environment-variable path** —
`DOTNET_SYSTEM_DIAGNOSTICS_ACTIVITY_DEFAULTIDFORMAT=W3C` — since that is the
documented route for services whose code cannot be touched, and the analyzer
structurally cannot see it.

### 2 — gRPC exporter

**Reproduce.** Configure OTLP over gRPC. Exercise the fixture.

**Expect.** No spans at all, and no error surfaced to the application.

**The question this answers.** ADR-0005 concedes an analyzer cannot catch this
when the protocol is set by environment variable. What is unknown is whether
*anything* observable happens — a dropped connection, a log line at some
verbosity level, an SDK-internal metric. If something is observable, it becomes a
stack-health signal instead of a Gate 1 eyeball check, and ADR-0005 should be
revised to say so.

**Then.** Switch to `http/protobuf` on 4318 and confirm spans arrive.

### 3 — Provider not disposed

**Reproduce.** Omit `_telemetry?.Dispose()` from `Application_End`. Generate
spans, then recycle the application pool.

**Expect.** The final batch is lost — which is exactly the telemetry from a
crash.

**Then.** Add disposal, repeat the recycle, confirm the batch arrives. Confirm
the ADR-0005 analyzer rule errors when the `IDisposable` is not stored and
disposed.

**Also confirm idle-timeout behaviour.** Rev 3 **F-I3** notes an idle-shutdown
pool stops emitting entirely, which registers as a **coverage** miss under
**I4.6** rather than as an error.

## Real-service confirmation

One service, one maintenance window. Confirm:

- [ ] `traceparent` present on a real outbound request — inspect the header itself
- [ ] One `trace_id` spans .NET 10 → HTTP → 4.8 → SQL
- [ ] Spans survive a manual application pool recycle
- [ ] `OTEL_*` set in exactly one place — never both `Web.config` and environment
- [ ] Static content filtered out of traces
- [ ] Integrated pipeline mode confirmed; not ARM64

## Outcomes

| Result | Action |
|---|---|
| All three behave as documented | Implement the ADR-0005 rules as specified |
| A failure mode behaves differently | **Revise ADR-0005 first**, then implement |
| A failure mode does not reproduce | Record why. A rule guarding a condition that cannot occur is noise, and noise trains people to ignore diagnostics |
| Failure 2 turns out to be observable | Revise ADR-0005 and route it to the stack health dashboard |
