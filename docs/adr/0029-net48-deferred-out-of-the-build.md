# 29. .NET Framework 4.8 is deferred out of the build entirely

Date: 2026-09-08

## Status

Accepted — reverses [ADR-0012](./0012-net10-first-sequencing.md), deviates from
Rev 3

## Context

[ADR-0012](./0012-net10-first-sequencing.md) had both packages multi-target
`net48;net10.0` from the first commit, on the argument that the net48 build
failing is the cheapest available test of a constraint that is expensive to
retrofit. [ADR-0022](./0022-demo-first-resequencing.md) then deferred 4.8
*validation* past the demo, leaving the target built but unexercised.

Thirteen months of that arrangement produced a specific shape. The net48 target
compiles, and nothing else about it is true:

- **Issue 11** — CouchDB URL redaction and `_changes` filtering were absent from
  the 4.8 path entirely for the life of the repository. The code now exists and
  is verified on .NET 10 only.
- **Issue 12** — ADR-0005's trace-context metric did not exist on either
  runtime. It now exists, verified on .NET 10 at value 0. The value-1 case *is*
  the .NET Framework default, so the runtime the metric was specified for is the
  one it is unproven on.
- **ADR-0005**'s three analyzer rules are specified and deliberately unbuilt,
  pending Phase 2 observation that has not happened.
- **ADR-0028** gives log records one enforcement point on 4.8 against two on
  .NET 10, a stated deviation from Rev 3 I3.2.

Every one of those closes the same way: a real 4.8 process, against real
dependencies, with someone reading the telemetry it produced. None of them close
by compiling. The build target was carrying the *appearance* of 4.8 progress
while all four of the things that matter stayed open, and it made every change
to the mechanism layer cost a second target's worth of conditional code and
package divergence for that appearance.

The .NET 10 path is not finished either. Dashboard 3 cannot be built at all,
the `messaging.*` keys are not enumerated, and thirteen of thirty specified
panels have a data source. Splitting attention across two runtimes while the
first one is incomplete is what produced the state above.

## Decision

**4.8 is deferred entirely — out of the build, not merely unvalidated.** Both
packages target `net10.0` only.

Work on 4.8 resumes when .NET 10 is complete and verified end to end: a service
integrated per `docs/onboarding/integrating-a-service.md`, telemetry surviving
both enforcement points, and the four dashboards built from
`docs/diagnostic-queries.md` against real data. Not before.

The net48 sources stay in the tree behind `#if NETFRAMEWORK`
(`RaksawiObservability.net48.cs`), and the package references 4.8 needs stay in
`Raksawi.Observability.csproj` as a commented `ItemGroup`. Restoring the target
framework without them fails the build rather than silently dropping ASP.NET
instrumentation.

## Consequences

- **This is the retrofit cost ADR-0012 warned about, accepted deliberately.**
  That ADR's argument was correct and is not disputed here: an API that diverges
  by target is discovered late and expensively. Issue 11 is the worked example —
  `FilterHttpRequestMessage` versus `FilterHttpWebRequest`, found only because
  the target was still building. Future divergences of that kind will now be
  found when 4.8 resumes, in a batch, rather than one at a time at the commit
  that caused them.
- 🔒 **The net48 sources rot from here.** They are not compiled by anything, so
  they are not checked by anything — not the compiler, not `dotnet format`, not
  the analyzer, not `TreatWarningsAsErrors`. Treat that file as a record of what
  was written, not as working code, and expect it to need repair rather than
  review when 4.8 resumes.
- Issues 11 and 12 are **blocked, not merely open**. Neither can be worked on
  until this decision is reversed, and both say so.
- The three enforcement points still hold for every service the estate ships
  *today*, because no 4.8 consumer exists — the same boundary ADR-0012 and
  ADR-0022 relied on. A 4.8 service arriving before this is reversed would have
  the collector alone, fail-closed
  ([ADR-0009](./0009-governing-agent-instrumented-services.md)), which is the
  agent-path posture rather than a gap.
- **Deviates from Rev 3**, which sequences the estate's .NET Framework services
  as in-scope rather than postponed behind a runtime gate. Rev 3 **F-I5** notes
  they are the highest-risk services in the estate despite being the fewest, and
  this decision extends the period in which they have no library-side control at
  all. It is a sequencing deviation, not a scope one: nothing here says 4.8 is
  out of scope, and ADR-0005, ADR-0012 and ADR-0028 are unamended and waiting.
- The reversal is a package release like any other. Restore the two
  `TargetFrameworks` lines and the commented `ItemGroup`, then expect issue 11's
  and issue 12's verification requirements to be the first work, not the last.
