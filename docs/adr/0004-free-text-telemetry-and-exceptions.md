# 4. Free-text telemetry: interpolation banned at build, scanned at the collector

Date: 2026-08-10

## Status

Accepted

## Context

[ADR-0003](./0003-runtime-allowlist-at-source.md) put every span and log
*attribute* behind a runtime allowlist. That control is keyed on attribute
names, so it does not reach two channels that carry free text:

**Log message bodies.** Rev 3 **D3.2** contrasts
`logger.LogInformation("Screening failed for {Email}", email)` with the
structured form. The `{Email}` case is a structured attribute and the allowlist
stops it. The genuinely uncontrolled case is
`logger.LogInformation($"Screening failed for {email}")` — interpolated into the
message, producing regulated data in free text and no attributes at all. The
allowlist has nothing to drop. `IncludeFormattedMessage = true` (N-D5) then
ships it. Rev 3 **F-D8** notes legacy 4.8 code is more likely to be written this
way, not less.

**Exception messages.** `RecordException = true` attaches `exception.message`
verbatim. Rev 3 **D3.1** already lists "exception messages not leaking record
contents" as an audit check. SQL exceptions are the acute case: constraint
violations and conversion errors quote row values by design — a unique-key
violation names the duplicate values, which on a KYC schema means applicant
names and addresses.

Dropping formatted messages wholesale was considered and rejected: it is
categorically safe and hostile to MTTR, which is the project's stated goal
rather than a side benefit.

## Decision

**Log bodies.** The analyzer rejects interpolated strings and string
concatenation passed as a log message or template. The collector pattern-scans
log bodies and exception messages for known regulated shapes — CPR, MRZ, email.

**Exceptions.** `RecordException` stays enabled for application and HTTP
instrumentation, and is **disabled for SQL client instrumentation**.

**Amended 2026-08-10 — the database is CouchDB, not SQL.** See
[ADR-0023](./0023-couchdb-changes-the-database-surface.md). There is no SQL
client, so the disabling rule has nothing to apply to and the acute case
described above does not arise. CouchDB errors are HTTP status codes with JSON
bodies rather than messages quoting row values.

Everything else in this ADR stands unchanged: the interpolation ban, the
collector pattern scanning, and `RecordException` remaining enabled elsewhere.
The rule is kept rather than deleted, so that a SQL component arriving later
inherits it instead of being governed by nothing.

## Consequences

- This is the first control where the collector does primary work rather than
  acting as the net described in Rev 3 **I3.2**. Accepted deliberately: for free
  text there is no source-side control that is both effective and affordable.
  ADR-0003 rejected pattern scanning at source because it is unbounded work on
  the request path; the same scanning is affordable at the collector, where it
  costs platform CPU and cannot threaten the **I3.6** invariant that telemetry
  never participates in business request success or failure.
- The analyzer rule covers first-party code only, and cannot see exceptions at
  all. Exceptions are covered solely by collector scanning, which catches shapes
  it knows and will miss novel ones. This residual is accepted and named here so
  the **D3.1** audit inherits it rather than rediscovering it.
- ~~Disabling `RecordException` on SQL makes that layer internally consistent~~ —
  **void as of ADR-0023.** There is no SQL client. The database risk did not
  vanish, it moved to `url.full` on CouchDB spans, where this ADR's mechanisms
  do not reach and the allowlist carve-out does.
- The diagnostic loss this ADR accepted for SQL failures is not incurred, since
  those failures do not exist here.
