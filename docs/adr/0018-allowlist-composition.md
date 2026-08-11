# 18. The allowlist is families with carve-outs, validated empirically

Date: 2026-08-10

## Status

Accepted

## Context

[ADR-0003](./0003-runtime-allowlist-at-source.md) drops any attribute whose key
is not allowlisted. That makes the *contents* of the allowlist load-bearing in
both directions: an incomplete list silently deletes legitimate telemetry, and an
over-broad one defeats the control.

The instrumentation in scope — ASP.NET Core, HttpClient, SqlClient, Runtime,
NATS.Net — emits attribute keys in the low hundreds. OpenTelemetry semantic
conventions version those names: `http.method` became `http.request.method`,
`db.statement` became `db.query.text`. An allowlist pinned to the wrong semconv
version drops everything.

Two pure approaches were considered. Enumerating from the specification is
complete on paper but allowlists what the spec says rather than what the packages
emit, and Rev 3 **N-D1** notes several of these ship as prerelease contrib
packages. Capturing empirically from a running fixture reflects reality but only
covers exercised paths — exception handling, redelivery, connection failure emit
keys never seen in a happy-path run, and those are the paths that matter during
an incident.

## Decision

Allow by family, deny by carve-out, then validate empirically.

**Families allowed:** `http.*`, `db.*`, `network.*`, `messaging.*`, `code.*`,
`server.*`, `client.*`, `user_agent.*`, plus the Class 0 and Class 1 families
already allowed by ADR-0003 and the Class 2 keys declared by policy packs per
[ADR-0017](./0017-allowlist-declared-as-assembly-attributes.md).

**Carve-outs denied within those families**, as the security-critical artifact:

| Key | Reason |
|---|---|
| `http.request.header.*` | Class 4 — includes `Authorization`. Opt-in upstream, so this denies a future enablement rather than current behaviour |
| `http.response.header.*` | Class 4 — includes `Set-Cookie`. Same opt-in status |
| `url.full`, `url.query` | Class 3 — identifiers per Rev 3 **D0.4b** |
| `db.query.text`, `db.statement` | What `SetDbStatementForText = false` exists to suppress |
| `db.query.parameter.*` | Parameter values are row data |
| `exception.message` on SQL spans | Per [ADR-0004](./0004-free-text-telemetry-and-exceptions.md) |

The targeted **semantic convention version is pinned** to a dated release and
treated as schema under Rev 3 **D2.7**; changing it is a versioned change with a
changelog entry.

Within that pin, stable families are allowed by prefix and need no attention on
upgrade, while **experimental attributes the estate depends on are enumerated
individually**. This matters most for `messaging.*`, which has moved repeatedly
and which the NATS instrumentation depends on: a stable-only allowlist would drop
most messaging telemetry, and an unpinned one would let it change underneath us.

The renames are not hypothetical — `http.method` became `http.request.method`,
`http.url` became `url.full`, `http.status_code` became
`http.response.status_code`, and the `net.*` attributes became `network.*`,
`server.*`, and `client.*`. An allowlist written against the older names drops
everything on upgrade.

The drafted families and carve-outs are recorded in
[`docs/allowlist.md`](../allowlist.md).

Empirical validation runs against the Phase 1 fixture: dump every key actually
emitted, reconcile against the families and carve-outs, and classify anything
unaccounted for. This is a **Gate 2** item, since the fixture does not exist yet.

## Consequences

- Unexercised code paths are covered by construction. This is the property
  capture-only would not have had, and it matters most on error paths.
- The carve-out list is where this design can fail dangerously, because a family
  allow is broad by intent. It is reviewed as security-critical, not as
  configuration, and additions to it are the same class of change as adding a
  metric dimension under Rev 3 **D2.1**.
- The dropped-key metric from ADR-0003 is the feedback loop that makes an
  incomplete allowlist visible. Without it, a family that failed to cover a real
  key is indistinguishable from instrumentation that is not running.
- Pinning the semconv version means an instrumentation package upgrade can break
  the allowlist. That is intended: the break is a build or dashboard signal
  rather than a silent change in what is captured.
- 🔒 The families are permissive by default within their prefix. The compliance
  argument rests entirely on the carve-outs being right, so Rev 3 **D3.1**'s
  audit should treat the carve-out list as its primary object rather than
  reviewing keys one at a time.
