# 42. Database instrumentation is opt-in per database

Date: 2026-11-18

## Status

Accepted. Deviates from the neutral-observability plan's order (D7 after D6b),
not from Rev 3.

## Context

Circle, the first service outside this repo to adopt the package, keeps its
data in SQL Server and had to add `AddSqlClientInstrumentation()` beside
`AddRaksawiObservability(...)` by hand (circle-integration issues 01 and 03).
That is the per-service assembly ADR-0001's one call exists to prevent.

The estate runs SQL Server, Postgres and CouchDB, and which one varies by
service. Registering SQL Server instrumentation in core would load it into
every Postgres and CouchDB service. The always-registered NATS source is
already that mistake in a cheaper form.

Governance needed no change. Circle's run showed the in-process allowlist
dropping `db.query.text` from SqlClient spans, as the Class 3 carve-out
requires. Only the registration was missing.

## Decision

Each database gets its own integration package, which the service opts into:

| Database | Package | Call |
|---|---|---|
| SQL Server | `Raksawi.Observability.SqlClient` (on `OpenTelemetry.Instrumentation.SqlClient`) | `builder.AddRaksawiSqlClient()` |
| Postgres | `Raksawi.Observability.Npgsql` (on `Npgsql.OpenTelemetry`), added 2026-11-19, see the amendment | `builder.AddRaksawiNpgsql()` |
| CouchDB | none: HTTP, covered by core's HttpClient instrumentation and `CouchDbUrlPolicy` | |

- Core references no database instrumentation. The test
  `Core_references_no_database_instrumentation` pins that.
- An integration adds instrumentation only, never a processor. The
  allowlist processor stays last before the exporter (ADR-0003), so
  integration spans are governed like every other span, with no
  per-integration policy.
- The package version follows `$(OpenTelemetryVersion)`, like core's
  instrumentation packages.
- Tracing only. SqlClient's client-duration metric is not registered: its
  dimensions have not been reviewed as metric dimensions.

The keys the SQL Server span carries were taken from a dump (ADR-0018), not
from documentation. On 2026-11-18, with `OpenTelemetry.Instrumentation.SqlClient`
1.17.0 against SQL Server 2022, the span was named `SELECT`, kind Client, with
`db.system.name`, `db.namespace`, `db.query.summary` and `server.address`.
`db.query.text` was dropped in-process and counted.

## Deviation from the plan's order

The neutral-observability plan builds integration packages in D7, after D6b
freezes the core contracts (C1 contributions, C2 configuration). This one is
built before D6b, because circle showed the gap now and ticket 01 recommended
doing the SQL work first. Cost: when D6b defines how an integration
contributes to the pipeline, `AddRaksawiSqlClient` may have to be reshaped to
fit it. The call is one line, so the rework is small.

## Consequences

- `samples/Ledger.Api` is a non-KYC SQL Server service on the mechanism layer
  and this integration. `e2e-instrumented.sh` runs it against SQL Server and
  asserts the dumped keys present, the statement text absent under either
  convention key, and the in-process drop counted.
- `Microsoft.Data.SqlClient` 6.1.7, used by the sample, pulls in a
  vulnerable `System.Security.Cryptography.Pkcs` 9.0.11. The sample pins
  10.0.13, as circle does. A service adopting this integration will meet the
  same advisory.
- A service on SQL Server needs one more call than a service on CouchDB.
  That is the price of not loading database instrumentation it does not use.

## Amendment, 2026-11-19: Postgres, and a connection-string carve-out

`Raksawi.Observability.Npgsql` follows the same shape. Npgsql instruments
itself, so the package only subscribes to its `Npgsql` source, and it is
versioned with Npgsql (10.0.3), not with OpenTelemetry. `samples/Payments.Api`
runs it in `e2e-instrumented.sh` against Postgres 17.

From the dump: Npgsql names the query span after the system (`postgresql`)
rather than the statement, and adds a `CONNECT postgres` span. Both are kind
Client, with `db.system.name`, `db.namespace` and `server.address`, plus
`db.npgsql.connection_id` and **`db.npgsql.data_source`**. `db.query.text`
was dropped and counted. There is no `db.query.summary`.

`db.npgsql.data_source` is the connection string. Npgsql strips the password
by default and keeps it under `Persist Security Info=true`, so a credential
was one setting away from passing both enforcement points under the `db.*`
family. **It is now a carve-out**, along with the general-form
`db.connection_string`, following the `db.statement` precedent: denied
in-process (`AllowlistRules.DeniedKeys`) and at the collector for spans, logs
and metric dimensions. The contract test keeps the two in step. Nothing
diagnostic is lost: host and database are already `server.address` and
`db.namespace`.
