Status: done 2026-11-19 (logs read back via ticket 02; frictions resolved or owned by the plan)
Priority: high

# Integrate the package into the circle repo and test it end to end

Raised 2026-11-10. The package has never run in a service outside this repo.
Every claim about how well it integrates rests on the Screening reference
service, which was written alongside it. The circle repo
(`C:\Users\MOGAMBO\Documents\MyDocs\Repository\circle`,
`TreasuryServiceOrchestrator.slnx`) is a real .NET solution that is not a KYC
service, so it is the first honest test.

## Why high priority

The plan spends about 7.5 engineer-weeks (D6a to D8b) splitting the package
into a neutral core, on the argument that a non-KYC service "cannot adopt it
cleanly" today (`docs/plans/neutral-observability-plan.md`, Context). That
argument has not been tested. A day or two here shows which frictions are real
before those weeks are spent.

## Blocked on

This session could not read the circle repo: reads outside the observability
folder are blocked, and `/add-dir` is not available over Remote Control. Start
a session with the circle folder added (run `/add-dir` from the local terminal,
or open the session in a folder that contains both repos).

Known so far, from one directory listing only: the solution file above, `src`,
`tests`, a local `packages` folder, `nuget.config`, central package management
(`Directory.Packages.props`), `ARCHITECTURE.md`, `CONTEXT.md`. Nothing inside
those files has been read.

## Steps

1. Read the circle repo's `CLAUDE.md` if it has one, `ARCHITECTURE.md` and the
   host project. Record what it already uses for logging and telemetry, and
   whether it uses NATS or CouchDB.
2. Pack `Raksawi.Observability` as `2.0.0-preview.1` into a local feed.
   Reference it from one host project, on a branch in the circle repo.
3. Add the `AddRaksawiObservability(...)` call. Point `Otlp:Endpoint` at the
   local stack's collector, `http://localhost:4319`.
4. Run the service against the local stack
   (`docker compose -f deploy/docker-compose.yaml up -d --build --wait`) and
   exercise one real request path.
5. Check in Grafana: its traces in Tempo, its metrics in Mimir, its logs in
   Loki, under its own `service.name`.

## Frictions to confirm or rule out

Each one gets a file and line, or the run that showed it.

- The NATS source is registered even when the service has no NATS.
- CouchDB host knowledge is required or carried for a service with no CouchDB.
- `ServiceNamespace` and `SamplingRatio` are mandatory outside Development.
- Standard `OTEL_*` environment variables are ignored.
- The service's own span attributes and log properties are dropped until
  declared. Find out whether it can declare keys in its own assembly, or needs
  a strong-named policy pack from this repo (ADR-0017 provenance).
- The collector allowlist is hand-written, so the service's declared keys must
  also be added to `deploy/collector/config.yaml`.
- Existing hand-wired OpenTelemetry or logging in the circle repo conflicts
  with the package (analyzer rule RKS003).
- Central package management in the circle repo against the package's own
  dependency versions.

## Done when

- One circle host project runs with the package and its traces, metrics and
  logs are read back from the local stack.
- Every friction above is marked hit or not hit, with evidence.
- A short finding says whether D6a to D8b is justified as planned, can be cut
  down, or is not needed.

## Rules for the work

- Work on a branch in the circle repo. No commit or push there without asking.
- If the circle repo already wires OpenTelemetry by hand, show the conflict
  before replacing anything.
- No change to the package in this repo as part of this ticket. A friction that
  needs a package change becomes its own ticket.

## Comments

### 2026-11-17 — run and findings

**Run.** Circle branch `spike/raksawi-observability`, uncommitted: the
package referenced from `TreasuryServiceOrchestrator.Api` and
`AddRaksawiObservability(...)` in place of the hand-wired tracing, SqlClient
added beside it. Circle running on `localhost:5068` (fake gateways, throwaway
SQL container `tso-spike-sql`), exporting to the `raksawi-probe` stack.
`GET /v1/accounts` x30, then read back through Grafana:

- **Traces: yes.** Tempo returns `GET v{version:apiVersion}/accounts` under
  `service.name = TreasuryServiceOrchestrator`, `service.namespace = treasury`,
  with its SqlClient children.
- **Metrics: yes.** Mimir holds 52 series names under
  `job="treasury/TreasuryServiceOrchestrator"`: ASP.NET Core, Kestrel,
  HttpClient, runtime, `target_info`.
- **Logs: no.** Loki counts zero lines for that `service_name` over 24 h,
  while screening-api and screening-worker each have about 58,000.

| Friction | Result | Evidence |
|---|---|---|
| NATS source registered without NATS | Hit, harmless | `RaksawiPipeline.cs:46` always adds `NATS.Net`; with no NATS client loaded it emits nothing |
| CouchDB knowledge required | Not hit | `CouchDbHosts` (`RaksawiObservabilityOptions.cs:51`) left empty; circle starts and exports |
| `ServiceNamespace` mandatory | Hit | `RaksawiObservabilityOptions.cs:93`; test `Service_namespace_is_required`. Circle hard-codes `"treasury"` |
| `SamplingRatio` mandatory outside Development | Hit (by source, not run: circle ran in Development) | `RaksawiObservabilityOptions.cs:99`; test `Absent_sampling_ratio_fails_outside_development` |
| `OTEL_*` variables ignored | Hit, partly | Endpoint always set from options (`RaksawiPipeline.cs:176`), so `OTEL_EXPORTER_OTLP_ENDPOINT` is ignored; sampler always set (`RaksawiPipeline.cs:44`), so `OTEL_TRACES_SAMPLER` is ignored. `OTEL_SERVICE_INSTANCE_ID` is honoured (`ServiceIdentity.cs:76`) and `OTEL_RESOURCE_ATTRIBUTES` through `ResourceBuilder.CreateDefault()` |
| Own attributes dropped until declared; can declare in own assembly? | Hit by source; no own keys seen in the run | A declaration counts only from an assembly signed with this repo's key (`AttributeAllowlist.cs:74`, `:213`; test `An_unsigned_assembly_cannot_declare_its_own_key`). Circle cannot declare its own keys; it needs a pack from this repo. In the run the only drop was `db.query.text` (349,781 counted), the Class 3 carve-out working as intended. Whether circle has its own `ActivitySource`s is unchecked: reading circle source is blocked in this session |
| Collector allowlist hand-written | Hit by source | `deploy/collector/config.yaml:154` is a literal regex; a key circle declared would also need adding there |
| Existing hand-wired OpenTelemetry conflicts (RKS003) | Not hit | The analyzer is a separate package, not pulled in, so RKS003 never ran. The package's `AddOpenTelemetry()` and circle's extra `AddOpenTelemetry().WithTracing(...)` share one provider without error |
| Central package management vs dependency versions | Not hit | Circle pins OpenTelemetry 1.19.x, the package asks for 1.17.0; restore and build clean (circle `check.sh`, 2026-11-13) |
| **Serilog** (new) | **Hit** | Zero log lines in Loki (above). Circle uses `Serilog.AspNetCore`; `UseSerilog` replaces the logging providers, so the package's OpenTelemetry logger provider receives nothing. Cause inferred from that, not proven by a run without Serilog |
| **SqlClient not in the package** (new) | Hit | Circle had to add `AddSqlClientInstrumentation()` beside the package. The estate is SQL/CouchDB (CLAUDE.md), and only CouchDB is covered |
| **Exporter noise** (new) | Hit (2026-11-13 run) | Circle's HTTP client logging recorded the package's own OTLP exports: most of 1,727 log lines in one run |

**Finding on D6a to D8b.** The plan's premise, that a non-KYC service "cannot
adopt it cleanly", is not borne out. Circle adopted the package with one call
and one extra option, and the NATS and CouchDB carry-over cost nothing. The
frictions that hurt are not the ones D6a and D7 restructure for:

1. **Logs do not arrive from a Serilog service.** In no deliverable; probably
   the most common logging setup in the estate. Needs a decision (a Serilog
   sink or bridge, or `writeToProviders`), and a ticket.
2. **SqlClient** is the estate's main database and is not in the package.
3. **`OTEL_*` precedence** (endpoint, sampler): D6b already covers it.
4. **A service cannot declare its own keys** without a pack signed by this
   repo, and the collector regex is hand-written. D8a/D8b and D9a cover these.

Recommendation: D6a and D7 (neutral-core slice and splitting out NATS/CouchDB)
can be cut down or deferred; nothing in this run needed them. Do the Serilog and
SqlClient work first, keep D6b's `OTEL_*` table, and keep D8 and D9 for
governance. This is a recommendation for the plan owner, not a change to the
plan.

**Left running:** circle on `:5068`, `tso-spike-sql`, the `raksawi-probe`
stack. The circle branch is uncommitted.

### 2026-11-19 — done

The three done-when items:

- **Traces, metrics, logs read back.** Traces and metrics on 2026-11-17 (above);
  logs on 2026-11-18 once circle used `writeToProviders` with `ClearProviders`
  (ticket 02). All three by manual query against the local stack; no script
  in either repo asserts on circle.
- **Frictions.** 1, Serilog: hit, resolved (ticket 02, recipe in
  `samples/README.md`). 2, SqlClient: hit, resolved (tickets 03 and 04,
  ADR-0042; circle on `AddRaksawiSqlClient` since circle `d60e96c`).
  3, `OTEL_*` precedence: hit, owned by D6b. 4, service-declared keys: hit,
  owned by D8a/D8b and D9a.
- **Finding on D6a to D8b:** above, unchanged. Cut down or defer D6a and D7;
  keep D6b, D8, D9. The decision is the plan owner's.

The 2026-11-17 "left running" note is stale: circle, `tso-spike-sql` and the
`raksawi-probe` stack are stopped, and the circle branch is committed and
pushed.

### 2026-11-19 — friction 3 resolved

The `OTEL_*` environment step of D6b landed (`4315b46`, ADR-0034), and the
packages were packed as `2.0.0-preview.3`. Circle runs on preview.3. It no
longer sets an endpoint in code unless `OpenTelemetry:OtlpEndpoint` is
configured, and the `localhost:4317` default is gone: that is the gRPC port,
which the package never used. Circle was run with its endpoint only in
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4319`, and Tempo returned its
traces after the process started, SQL spans included. That check was a manual
query; no script asserts on circle. Circle sets no sampler in code, so
`OTEL_TRACES_SAMPLER` now applies to it as well.
