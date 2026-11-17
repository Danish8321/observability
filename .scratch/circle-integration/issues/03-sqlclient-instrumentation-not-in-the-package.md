Status: open
Priority: medium

# SqlClient instrumentation is not in the package

Found 2026-11-17 in the circle spike (issue 01). The estate stores data in
SQL and CouchDB (CLAUDE.md); the package covers CouchDB (`CouchDbUrlPolicy`)
and not SQL. Circle added `AddSqlClientInstrumentation()` beside the package
by hand, which is the per-service assembly ADR-0001 says the one call exists
to prevent.

In the run the allowlist dropped `db.query.text` from those spans (349,781
counted), as the Class 3 carve-out requires, and `db.query.summary`,
`db.namespace` and `db.system.name` reached Tempo. So the governance already
holds for SQL spans; only the registration is missing.

## Decide

Register SqlClient in the core call, or in an integration package next to
the NATS and CouchDB ones (D7). Either way the run's evidence carries over.
