Status: open
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
