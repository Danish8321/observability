Status: done 2026-11-19 (analyzer rule RKS004)
Priority: medium

# A histogram in seconds gets millisecond buckets unless it declares advice

Found 2026-11-16 building the "Screening slow" alert (plan D4).
`screening.duration` (unit `s`) arrived in Mimir with boundaries 0, 5, 10,
25 … 10000, the OpenTelemetry .NET SDK defaults, which are meant for
milliseconds. Every screening fell in the first bucket, so p95 read 4.75 s
whatever the real duration. The sample now declares
`HistogramBucketBoundaries` advice (`samples/Screening.Domain/ScreeningTelemetry.cs`),
and `ScreeningTelemetryTests.Duration_buckets_resolve_seconds` holds it.

Any service in the estate that creates its own histogram with unit `s` and no
advice has the same problem. ASP.NET Core's and HttpClient's own duration
histograms declare advice and are fine.

## Options

- The package adds a view: any histogram with unit `s` and no advice gets the
  semconv second boundaries. A package change, so after D6b's contracts.
- An analyzer rule: `CreateHistogram` with unit `s` and no advice fails the
  build (the same shape as RKS003).
- Documentation only (`samples/README.md` now says it).

## Done when

A service that forgets the advice either gets usable buckets or fails its
build, with a test showing which.

## Comments

### 2026-11-19 — done, option 2 (analyzer rule)

Chosen over the view because it needs no package change ahead of D6b, and
over documentation alone because the done-when asks for a build failure.

- `RKS004`, Warning like RKS001/RKS003: `Meter.CreateHistogram` with a
  literal unit `"s"` and advice absent, defaulted or a literal `null`.
  Literal-only like RKS001: a computed unit raises nothing.
- Tests: `TelemetryGovernanceAnalyzerTests` (three raise, four do not).
  `test-fast.sh` 249/249, `check.sh` clean.
- Real build: with the advice removed from `ScreeningTelemetry.cs`,
  `dotnet build samples/Screening.Domain` failed with `error RKS004`
  (warning promoted by `TreatWarningsAsErrors`). Reverted afterwards.
- Limit: a service without `TreatWarningsAsErrors` gets a warning, not a
  failed build. The view (option 1) would cover it at run time; revisit
  after D6b if that proves to matter.
