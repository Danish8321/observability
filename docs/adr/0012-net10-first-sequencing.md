# 12. .NET 10 first, multi-targeted from the first commit, 4.8 validated in Phase 2

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 sequences both runtimes together. **Gate 1**'s acceptance criterion is a
single unbroken trace spanning .NET 10 → HTTP → .NET 4.8 MVC → SQL, with
`traceparent` confirmed on outbound 4.8 requests and log correlation on both
runtimes.

Working both runtimes at once is more than the available capacity wants to carry
at the start, and the .NET 10 half is where most of the estate lives.

Against that, Rev 3 **F-I5** states the legacy services are the highest-risk in
the estate despite being the fewest: all three of their failure modes are silent,
and the code is the least familiar. Deferring them moves the largest unknown to
the end of the schedule.

A separate risk sits underneath the sequencing question. Building the library
`net10.0`-only and adding `net48` later is not the same decision as validating
.NET 10 first. `net48` has no `IHostApplicationBuilder`, no `ILogger`-to-OTel
path — Rev 3 **F-D8** requires Serilog — a different default `Activity` ID
format, and an older language version. An API surface designed only against
.NET 10 idioms may have no `net48` shape at all, at which point
[ADR-0001](./0001-two-layer-observability-package.md)'s guarantee that
governance is identical across runtimes by construction would no longer hold.

## Decision

Work .NET 10 first for instrumentation, fixtures, and end-to-end verification.

Both packages multi-target `net48;net10.0` from the first commit. The `net48`
build is expected to compile and is part of the default build, even before any
4.8 consumer exists.

4.8 **runtime** validation — the cross-runtime trace, `traceparent` on outbound
requests, provider disposal across an app-pool recycle — is deferred to Phase 2.

Gate 1 is recorded as **partial**, in writing, with the 4.8 rows left open, a
named owner, and a date. It is not treated as passed.

## Consequences

- The compiler becomes a continuous test of the constraint that is hardest to
  retrofit. A .NET 10 API shape with no `net48` equivalent fails the build the
  day it is written rather than in Phase 2.
- `#if` branches and some awkwardness appear before there is a 4.8 consumer to
  justify them. This is the price of the guarantee above.
- 🔒 The three silent 4.8 failure modes stay unproven while the library is
  designed. Their mitigation is the analyzer and startup rules already decided in
  [ADR-0005](./0005-enforcing-the-framework-wiring.md), which were specified from
  the documented failure modes rather than from observation. If Phase 2
  validation contradicts them, ADR-0005 is what gets revised.
- Recording Gate 1 as partial rather than redefining it keeps the gate meaningful.
  A gate that is quietly narrowed to what was achieved stops being a gate.
- Per [ADR-0009](./0009-governing-agent-instrumented-services.md), most of the
  4.8 estate is expected to use the zero-code agent, which needs no `net48`
  package at all. If Rev 3 **D0.3**'s inventory shows that no 4.8 service will be
  recompiled, the `net48` target loses its purpose and this decision, along with
  ADR-0001's multi-targeting, should be revisited.
