Status: open
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
