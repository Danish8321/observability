Status: open
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
