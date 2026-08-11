# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Governance, shared libraries, and platform config for observability (OpenTelemetry) across the Raksawi estate — .NET 10 and .NET Framework 4.8, NATS and HTTP, SQL/CouchDB. Goal is reduced MTTR, not "installed OpenTelemetry" as an end in itself.

**Authority**: the observability implementation plan Rev 3 owns policy/sequencing. This repo owns execution and records decisions in `docs/adr/`. Where Rev 3 and an ADR disagree, Rev 3 wins unless the ADR states the deviation explicitly (four such deviations, listed in `docs/adr/README.md#deviations-from-rev-3`). Nothing deviates silently.

Read `README.md` and `CONTEXT.md` first — README for scope/shape/sequencing, CONTEXT for ubiquitous language (mechanism vs policy layer, allowlist, family, carve-out, data class, the four correlation identifiers). ADRs in `docs/adr/` are the decision record; check the index (`docs/adr/README.md`) before assuming a design choice is undocumented.

Service code lives in *other* repos and consumes these packages from Azure Artifacts — this repo contains no service code except the `samples/` reference implementation.

## Commands

```sh
.claude/scripts/check.sh       # restore, build all target frameworks (Release), dotnet format --verify-no-changes
.claude/scripts/test-fast.sh   # dotnet test -c Release — unit tests only, no collector/store/network
```

`test-full.sh`, `contract.sh`, `e2e.sh` are named in `README.md`'s verification table but not yet written — do not claim their evidence until they exist. Never claim "done"/"works" without running the applicable script above.

Single test: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.

No CLAUDE.md-level lint/format command beyond what `check.sh` runs (`dotnet format --verify-no-changes`).

## Architecture

**Two layers** (ADR-0001): mechanism knows how telemetry is produced/shipped and nothing about any business domain; policy knows what a specific business domain may say. A non-KYC service takes mechanism alone.

- `src/Raksawi.Observability/` — mechanism layer. `AddRaksawiObservability()` is the one-call .NET 10 entry point (`RaksawiObservabilityExtensions.net10.cs`, `#if NET10_0_OR_GREATER`); `RaksawiObservability.net48.cs` is the 4.8 counterpart. `ServiceIdentity.cs` builds the OTel resource and enforces W3C trace context. `CouchDbUrlPolicy.cs` redacts document IDs from `url.full` on CouchDB HTTP spans (exact host match, **fails open** — verify on a real span). `RaksawiObservabilityOptions.cs` is validated eagerly; an absent service name or sampler is a config error, not a telemetry outage (Rev 3 I3.6 — never fail service start for telemetry).
- `src/Raksawi.Observability.Kyc/` — policy layer for KYC services, depends on the mechanism layer. `DataClass.cs` encodes the 0–4 data-class bands (3/4 appear nowhere in telemetry; class 2 on traces/logs, never as a metric dimension). `KycTelemetry.SetApplicationId()` tags spans only — deliberately no metric equivalent (a per-application dimension is a memory leak with a dashboard, not a metric).
- Both packages multi-target `net48;net10.0` from the first commit (ADR-0012) even though .NET 10 is worked first — the net48 build failing is the cheapest test of a constraint expensive to retrofit later.
- `samples/` — the Screening reference service (`Screening.Api`, `Screening.Domain`, `Screening.Worker`): a worked KYC flow (`POST /applications` → screening-api → CouchDB, NATS publish → screening-worker → CouchDB, retry ×3 → abandon) showing the intended instrumentation patterns. Read `samples/README.md` before writing instrumentation code anywhere in the estate — it explains *why* each pattern exists (span naming, span kind on message hops, retry-as-event vs failure-as-status, abandonment as its own counter, not-found isn't an error, structured logs never interpolated (ADR-0004), Class 3 never emitted). Demo scaffolding (fault injection block, sampling 1.0, compose credentials) is explicitly not production-shaped (ADR-0022).
- `docs/allowlist.md`, `docs/diagnostic-queries.md` — the allowlist (families + carve-outs, ADR-0017/0018) and store-neutral diagnostic query specs (ADR-0016) are durable governance assets, reviewed separately from code.

**Three enforcement points, default deny at each** (ADR-0003, ADR-0009): analyzer at build, library before export, collector before storage. A process with none of our code has only the collector, fail-closed.

**Correlation identifiers**, each a different lifetime (see CONTEXT.md "Correlation"): `trace_id` (one request, SDK-minted), `session.id` (one browser page-load, carried as `X-Correlation-Id`), `correlation.id` (one business workflow, minted at workflow start — survives across traces), `causation.id` (direct parent message — distinguishes redelivery from retry), `message.id` (unchanged by redelivery).

**OTLP specifics**: http/protobuf on port 4318, not gRPC (4317 closed estate-wide; gRPC unsupported on the net48 target).

## Conventions

- `Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `Nullable` enable, `GenerateDocumentationFile` — a warning anywhere fails the build.
- Structured logging only (`LogInformation("Screened {ApplicationId}", id)`) — interpolated log strings are banned per ADR-0004, unqueryable and unredactable after the fact.
- Span names describe the work ("screen application"), not the method name — traces are read during incidents by people who didn't write the code.
- `ActivityKind.Producer`/`Consumer` on both sides of a message hop, or the two spans render as unrelated operations.
