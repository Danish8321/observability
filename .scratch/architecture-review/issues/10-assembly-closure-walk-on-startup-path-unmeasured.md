Status: closed — measured 2026-09-07

# The allowlist's assembly-closure walk runs on the startup path with no measured cost

`AttributeAllowlist.CandidateAssemblies()`
(`src/Raksawi.Observability/AttributeAllowlist.cs:110-160`) walks the transitive
reference graph of every loaded assembly plus the entry assembly, calling
`Assembly.Load` on anything not already present. It runs once, synchronously,
inside `AddRaksawiObservability` / `RaksawiObservability.Start`.

The reasoning for walking references rather than scanning loaded assemblies is
sound and well documented (the load-order trap is real: a policy pack
referenced but not yet used contributes nothing, and the analyzer would report
the code as correct while the runtime dropped the keys). Failure handling is
right too — both `GetCustomAttributes` and `Assembly.Load` are wrapped, so an
unresolvable reference cannot fail a service start (Rev 3 I3.6), and
`IsPlatform` keeps the BCL closure out.

## Impact

Two things are unquantified:

1. **Startup latency.** Forcing `Assembly.Load` across an application's
   non-platform reference closure is real work at boot, on every service in the
   estate. `docs/phase0/performance-baseline.md` (ADR-0014) records no figure
   for it. Rev 3 I3.6 is about not failing service start; a slow start is a
   different axis and is currently unmeasured, not known-good.
2. **Side effects of forced loading.** Loading an assembly the application
   would otherwise never touch runs its module initializer and any static
   constructor reached by it. Exceptions surface through `Assembly.Load` and
   are caught, so this cannot fail the boot — but the side effects still happen,
   earlier than the application intended.

Neither is a defect. Both are claims the repo currently cannot evidence, in a
repo whose verification contract says evidence is required.

## Fix

Measure once and record it:

- Number of assemblies force-loaded and wall time for `FromLoadedAssemblies()`
  on `Screening.Api` and `Screening.Worker`, added to
  `docs/phase0/performance-baseline.md` under the ADR-0014 method.
- If the figure is material, the mitigation is caching the result per AppDomain
  (it is already effectively immutable after startup) or restricting the walk
  to assemblies referencing the mechanism assembly — a reference the graph walk
  already has in hand.

Do not optimise before measuring.

## Measured (2026-09-07)

Recorded in `docs/phase0/performance-baseline.md`, new section "Startup cost of
the allowlist's assembly-closure walk", plus a five-line pointer in
`AttributeAllowlist.CandidateAssemblies`'s remarks so the figure is findable
from the code that costs it.

| Service | Loaded before | Force-loaded | Warm | First run after build |
|---|---|---|---|---|
| `Screening.Worker` | 59 | 8 | 4.8–5.3 ms | 289 ms |
| `Screening.Api` | 85 | 7 | 5.1–6.8 ms | 296 ms |

Method: temporary stopwatch + assembly-name diff inside `FromLoadedAssemblies`,
Release build, five runs from the built binary, instrumentation then removed —
it is not in the tree. Verified removed: `git diff` on `AttributeAllowlist.cs`
shows only the doc-comment addition.

Two findings beyond the number:

1. **The walk is load-bearing, not defensive.** `Raksawi.Observability.Kyc` is
   among the assemblies it force-loads — at `AddRaksawiObservability` time the
   policy pack genuinely is not loaded yet, so scanning loaded assemblies would
   have silently dropped every Class 2 key while the analyzer passed. The
   design's stated reason is now evidenced rather than asserted.
2. **The ~290 ms first run** is cold file-cache and JIT, reproducing once per
   fresh deployment. That is the number to quote for a container's first start;
   ~5 ms is the number for a restart.

Impact item 2 (side effects of forced loading) is **narrowed, not closed**: no
side effect was observed across the seven assemblies actually pulled in, which
is an observation about those seven, not a guarantee. Recorded as such.

Not optimised — at 5 ms warm, caching per AppDomain would be a fix for nothing.
The doc records the threshold (>50 ms warm or >20 forced assemblies on a real
service) and the two mitigations, in order, for whoever crosses it.

Closing here rather than holding for D0.3's three nominated services: the
ticket asked for the reference implementation's figure and the caveat that it
is a floor, and both are now written down. Re-measuring on estate services is
D0.2's job, and the doc says so in the section itself.

## Comments
