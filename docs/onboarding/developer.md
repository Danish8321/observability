# Onboarding: developing in this repo

For anyone working *on* `Raksawi.Observability`/`Raksawi.Observability.Kyc` themselves, or the samples/collector config in this repo. If you're consuming the package in a service, see [`integration.md`](./integration.md) instead.

## First, read

1. `README.md` — scope, shape, sequencing, current state
2. `CONTEXT.md` — glossary: mechanism/policy layer, allowlist, family, carve-out, data class, correlation vocabulary
3. `docs/adr/README.md` — index of every decision; skim the table before assuming something is undecided
4. Root `CLAUDE.md` — architecture map, commands, conventions (this file duplicates none of it — read it)

**Authority rule**: Rev 3 (the observability implementation plan) owns policy/sequencing. This repo owns execution. Where they disagree, Rev 3 wins unless an ADR states the deviation explicitly — four exist, listed in `docs/adr/README.md#deviations-from-rev-3`. Never resolve a conflict silently in code; write or update an ADR.

## Setup

```sh
git clone <repo>
cd observability
dotnet restore
```

Requires .NET 10 SDK (multi-targets `net48;net10.0` — see `Directory.Build.props`). No other tooling needed for the library/tests.

For running samples end to end (collector, NATS, CouchDB, SigNoz), see `samples/README.md`.

## Everyday commands

```sh
.claude/scripts/check.sh       # restore + build both target frameworks (Release) + dotnet format --verify-no-changes
.claude/scripts/test-fast.sh   # dotnet test -c Release — unit only, no collector/store/network
```

Single test: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.

`test-full.sh`, `contract.sh`, `e2e.sh` are named in `README.md`'s verification table but not written yet — don't claim their evidence until they exist. Never say "done"/"works"/"fixed" without one of these scripts backing it.

`Directory.Build.props` sets `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` — any warning fails the build, on both targets. Expect `net48` to occasionally break in ways `net10.0` doesn't; that's the point (ADR-0012) — it's the cheapest test of a constraint that's expensive to retrofit.

## Where things live

```
src/Raksawi.Observability/       mechanism layer — no business-domain knowledge
src/Raksawi.Observability.Kyc/   policy layer — depends on mechanism, KYC-specific
tests/                            unit tests, mirror src/ structure
samples/                          Screening.* — reference service, worked example + fault injection
deploy/                           collector config, docker-compose for demo infra
docs/adr/                         every decision, numbered, in order made
docs/allowlist.md                 families, carve-outs, Class 2 keys — reviewed governance intent
docs/diagnostic-queries.md        store-neutral panel/runbook specs
docs/phase0-3/                    programme worksheets, method decided, most data not yet collected
```

## Working on mechanism vs policy

Two layers, strictly separated (ADR-0001):
- **Mechanism** (`Raksawi.Observability`) — how telemetry is produced/shipped. No business vocabulary allowed in here. If you're tempted to reference "application ID" or a domain concept in this project, it belongs in a policy pack instead.
- **Policy** (`Raksawi.Observability.Kyc`, and any future `*.Kyc`-style pack) — what a specific domain may say. Depends on mechanism, never the reverse.

Multi-target split: shared logic goes in files without an `#if`; runtime-specific entry points are `#if NET10_0_OR_GREATER` (`RaksawiObservabilityExtensions.net10.cs`) or `#if NETFRAMEWORK` (`RaksawiObservability.net48.cs`). `ServiceIdentity.cs` is compiled by both deliberately — governance lives in shared compilation, not per-runtime docs (ADR-0001).

## Before adding or changing anything

- **New allowlist key or family** → this is a governance change, not just code. Update `docs/allowlist.md` and check the analyzer story (ADR-0002/0003/0017/0018) — a key without provenance is ignored, not silently allowed.
- **New attribute/tag anywhere in mechanism or policy code** → confirm its data class (0-4, `DataClass.cs` / CONTEXT.md). Classes 3/4 must never reach a span, log property, or metric dimension. A test should assert absence, the way `KycTelemetryTests.cs` asserts no metric-equivalent method exists for `SetApplicationId`.
- **Touching sampling, resource attributes, or OTLP config** → check ADR-0006 (identity), ADR-0008 (instance identity), ADR-0010 (sampling defaults) first; these are deliberately narrow and validated eagerly (`RaksawiObservabilityOptions.Validate()`).
- **Schema/persistence change** — not applicable today (no DB in this repo), but if it ever is, this repo's global CLAUDE.md requires `.claude/scripts/schema.sh` and reading every generated migration before applying.
- **New dependency** — ask first; justify in one line per the global discipline rules.
- Real architectural decision (not implementation detail) → write an ADR, don't just merge code. Follow the numbering/format of existing ones in `docs/adr/`.

## Testing conventions

- Unit tests only in `test-fast.sh` scope — no collector, no store, no network. `CouchDbUrlPolicyTests.cs`, `KycTelemetryTests.cs`, `RaksawiObservabilityOptionsTests.cs`, `ScreeningTelemetryTests.cs` are the current shape to match.
- A negative-space test (asserting a method/behavior does *not* exist, e.g. no metric equivalent for a Class 2 tag) is a legitimate and expected pattern here — it's how the "never a metric dimension" rule gets enforced outside the analyzer.

## Samples

`samples/Screening.*` is both a runnable demo and the canonical style reference for instrumenting an estate service — read `samples/README.md` end to end before writing new patterns; it explains the *why* behind each one (span naming, span kind on hops, retry-as-event, abandonment-as-its-own-counter, not-found isn't an error). Its fault-injection block and demo credentials are explicitly non-production (ADR-0022) — don't copy those parts.

## Current state (as of last check)

Design/governance heavy, code exists for the demo path. `README.md`'s "Current state" section and `docs/open-questions.md` are the live truth — check them, don't assume from this doc, they move faster than onboarding docs do.
