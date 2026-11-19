Status: done (ADR-0042 amendment, 2026-11-19)
Priority: medium

# Postgres integration package

Split from issue 03 on 2026-11-18. Same shape as the SQL Server integration
(ADR-0042): its own package, `Raksawi.Observability.Npgsql`, one call that
adds Npgsql's tracing to the provider core builds, and core loads none of it.

## Why its own ticket

- It needs a new dependency, `Npgsql.OpenTelemetry`, to be agreed.
- Which `db.*` keys Npgsql emits is unverified. ADR-0018 takes keys from a
  dump, so this needs a Postgres container and a service querying it before
  anything is asserted. `db.statement` and `db.query.text` are both
  carve-outs, so the statement text is covered on paper under either
  convention.

## Done when

- A Postgres-backed sample runs in `e2e-instrumented.sh` beside `ledger-api`,
  with the statement text absent after both enforcement points.
- The asserted keys are taken from that run's dump.
- `Core_references_no_database_instrumentation` still passes; it already
  checks for Npgsql.

## Comments

### 2026-11-19 — done

Dependency agreed by the owner ("go ahead as best"). `Npgsql.OpenTelemetry`
10.0.3 (needs Npgsql 10.0.3 and `OpenTelemetry.API` 1.15.3 or later, so it
fits 1.17.0).

- `src/Raksawi.Observability.Npgsql`, one call, `AddRaksawiNpgsql()`.
- `samples/Payments.Api` runs in `e2e-instrumented.sh` against
  `postgres:17-alpine`. Passed 2026-11-19, 52/52.
- Dump: the query span is named `postgresql` (after the system, not the
  statement), plus `CONNECT postgres`, both Client. Keys: `db.system.name`,
  `db.namespace`, `server.address`, `db.npgsql.connection_id`,
  `db.npgsql.data_source`. `db.query.text` was dropped and counted. There is
  no `db.query.summary`.
- **Found:** `db.npgsql.data_source` is the connection string, admitted by
  the `db.*` family at both enforcement points. Its password is stripped
  only by default. It is now a carve-out, with `db.connection_string`,
  in-process and at the collector. The e2e asserts it absent and counted.
  See the ADR-0042 amendment.
