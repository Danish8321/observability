Status: done for SQL Server (ADR-0042); Postgres moved to issue 04
Priority: medium

# Database instrumentation is not in the package

Found 2026-11-17 in the circle spike (issue 01). The estate stores data in
SQL and CouchDB (CLAUDE.md); the package covers CouchDB (`CouchDbUrlPolicy`)
and not SQL. Circle added `AddSqlClientInstrumentation()` beside the package
by hand, which is the per-service assembly ADR-0001 says the one call exists
to prevent.

In the run the allowlist dropped `db.query.text` from those spans (349,781
counted), as the Class 3 carve-out requires, and `db.query.summary`,
`db.namespace` and `db.system.name` reached Tempo. So the governance already
holds for SQL spans; only the registration is missing.

## Decided 2026-11-17: opt-in per database, not core

The database differs by product: SQL Server, CouchDB and Postgres all run in
the estate. Registering SqlClient in the core call would load SQL Server
instrumentation into every Postgres and CouchDB service, the same mistake as
the always-registered NATS source. So each database is an opt-in integration,
the D7 shape:

| Database | Instrumentation | Status |
|---|---|---|
| SQL Server | `OpenTelemetry.Instrumentation.SqlClient` | run on circle; `db.query.text` dropped as required |
| Postgres | Npgsql's own OpenTelemetry support (`Npgsql.OpenTelemetry`) | not run; which `db.*` keys it emits is unverified. `db.statement` and `db.query.text` are both carve-outs, so either convention is covered on paper |
| CouchDB | none: HTTP, covered by core's HttpClient instrumentation and `CouchDbUrlPolicy` | in the package |

This is evidence for D7, for a different reason than the plan gives: not
keeping KYC out of core, but the database varying by service.

## Done when

- SQL Server and Postgres integrations each run in a service in the local
  stack, with the statement text absent after the in-process allowlist.
- The Postgres keys are taken from a dump, not from documentation (ADR-0018).
- A service with neither loads neither.

## 2026-11-18 — SQL Server shipped; Postgres split out

Decided with the owner: new dependency agreed, plan-order deviation recorded
as ADR-0042, proof in a sample in this repo rather than only in circle, and
Postgres as its own ticket (issue 04).

- `src/Raksawi.Observability.SqlClient`, one call, `AddRaksawiSqlClient()`,
  on `OpenTelemetry.Instrumentation.SqlClient` at `$(OpenTelemetryVersion)`
  (1.17.0).
- `samples/Ledger.Api`, a non-KYC SQL Server service, runs in
  `e2e-instrumented.sh` against SQL Server 2022. Passed 2026-11-18, 44/44.
  The new assertions: SqlClient span present with `db.system.name`,
  `db.namespace` and `db.query.summary`; statement text absent; neither
  `db.query.text` nor `db.statement` as an attribute; SQL password absent;
  the in-process drop of `db.query.text` counted.
- Keys taken from that run's dump: span `SELECT`, kind Client,
  `db.namespace`, `db.query.summary`, `db.system.name`, `server.address`.
- "A service with neither loads neither": `Core_references_no_database_instrumentation`
  (test-fast) checks core references no SqlClient or Npgsql assembly.

Not done: circle still adds SqlClient by hand. Moving it onto
`AddRaksawiSqlClient()` needs a repacked preview, and that has not been done.
