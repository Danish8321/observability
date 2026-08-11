# 23. CouchDB moves the database risk from statement text to the URL

Date: 2026-08-10

## Status

Accepted — corrects a factual assumption in ADR-0003, 0004, and 0018

## Context

Every document in this repository written before 2026-08-10 assumed a SQL
database. It is **CouchDB**. Fifteen documents carry the wrong assumption.

The assumption was load-bearing for the compliance argument, not incidental.
`db.statement` and `db.query.text` were carve-outs in the allowlist, and
[ADR-0004](./0004-free-text-telemetry-and-exceptions.md) disabled
`RecordException` on SQL client instrumentation specifically because an ordinary
`WHERE Cpr = '...'` is a reportable disclosure sitting inside an exception
message.

CouchDB is HTTP/JSON. There is no SQL client, no statement text, and no
`db.*` semantic convention in play unless something adds one deliberately.

## Decision

**Instrumentation.** CouchDB access is instrumented by standard `HttpClient`
instrumentation as ordinary client spans. No database instrumentation package is
added, and no `db.*` attributes are emitted. If CouchDB calls need to be
distinguishable from other outbound HTTP, that is done by `server.address`,
not by inventing attributes.

**The carve-out moves.** `db.statement`, `db.query.text`, and
`db.query.parameter.*` cease to be the database carve-outs, because nothing
emits them. They stay in the allowlist as carve-outs regardless — costless, and
they prevent a future SQL component from arriving unnoticed.

The real database carve-out is now **`url.full` and `url.query` on CouchDB
spans**, which were already carved out for other reasons and are now doing
significantly more work:

| CouchDB shape | Where identity can appear |
|---|---|
| `GET /{db}/{docid}` | Document ID, in the URL path |
| `GET /{db}/_design/{d}/_view/{v}?key=...` | View key, in the query string |
| `POST /{db}/_find` | Mango selector, in the **request body** — not captured by default, and must remain so |

**One question decides how much work this is:** are CouchDB document IDs derived
from applicant data, or opaque? It is answered in demo Phase D0, before any
instrumentation exists.

**`_changes` is excluded from tracing.** The continuous feed is a long-poll HTTP
request held open for minutes. Traced as an ordinary client span it produces
spans of arbitrary duration that corrupt every latency percentile computed from
span data.

## Consequences

- **Answered 2026-08-11: document IDs are opaque, not derived from applicant
  data.** The exposure below did not materialize. `CouchDbUrlPolicy` redaction
  is retained as defense-in-depth rather than as a compliance-blocking fix —
  it still fails open, and that behavior should still be verified against a
  real span before being relied on (see `.scratch/demo-readiness/issues/01-*`).
- The SQL client instrumentation problem, and with it ADR-0004's
  `RecordException` decision for database spans, **no longer applies**. ADR-0004
  keeps its rules for exception messages generally; its database-specific
  reasoning is void.
- 🔒 The exposure did not disappear, it relocated. If document IDs are derived
  from applicant data, **every CouchDB call leaks identity into `url.full`** —
  which is a broader surface than SQL was, since it is on every read rather than
  only in captured statement text.
- That same exposure exists **today**, independent of this project: those URLs
  are already in CouchDB's own logs and in any HTTP proxy in front of it. If
  Phase D0 finds derived document IDs, that is a finding for the compliance
  team, not a task for this project.
- Fifteen documents contain stale SQL references. They are corrected where the
  meaning changes and left where "database" is incidental — recorded here so the
  inconsistency is understood rather than rediscovered.
- The estate inventory's Transports field gains CouchDB and drops SQL. The SQL
  DMV item in Run 0's discovery sources is replaced by CouchDB's own `_stats`
  and `_active_tasks` endpoints.
