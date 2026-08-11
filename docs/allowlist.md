# Attribute allowlist

**Status:** families and carve-outs drafted. Concrete key list **not validated** —
that requires the Phase 1 fixture and is a Gate 2 item per
[ADR-0018](./adr/0018-allowlist-composition.md).

**Shape:** [ADR-0018](./adr/0018-allowlist-composition.md) — allow by family, deny
by carve-out, validate empirically.
**Source of truth:** assembly attributes in the packages, per
[ADR-0017](./adr/0017-allowlist-declared-as-assembly-attributes.md). This document
is the reviewed statement of intent; the code is the enforcement.

---

## Pinned semantic convention version

**Target version:** *(to be set when the allowlist is first declared in code)*

Pinned deliberately. An instrumentation package upgrade that renames attributes
must produce a visible break, not a silent change in what is captured.

The renames are not hypothetical. Confirmed against the OpenTelemetry semantic
conventions repository:

| Was | Is |
|---|---|
| `http.method` | `http.request.method` |
| `http.status_code` | `http.response.status_code` |
| `http.url` | `url.full` |
| `http.resend_count` | `http.request.resend_count` |
| `net.peer.name`, `net.peer.port` | `server.address`, `server.port` |
| `net.protocol.name` | `network.protocol.name` |

An allowlist written against the left column drops everything on upgrade.

## Stability policy

Stable families are allowed by prefix and need no attention on upgrade.
Experimental attributes we depend on are **enumerated individually**, so that
churn in them is a deliberate review rather than a silent drop.

This matters most for `messaging.*`, which has moved repeatedly and which NATS
instrumentation depends on. A stable-only allowlist would drop most messaging
telemetry; an unpinned one would let it change underneath us.

## Allowed families

| Family | Class | Notes |
|---|---|---|
| `service.*` | 1 | Resource identity. See [ADR-0006](./adr/0006-service-identity-convention.md), [ADR-0008](./adr/0008-service-instance-identity.md) |
| `deployment.*`, `vcs.*`, `cicd.*` | 1 | Resource schema, Rev 3 Appendix B |
| `telemetry.sdk.*`, `process.*`, `host.*` | 0 | Infrastructure |
| `http.*` | 0/1 | Subject to carve-outs below |
| `url.*` | 0/1 | Subject to carve-outs below |
| `server.*`, `client.*`, `network.*` | 0 | Post-rename network attributes |
| `db.*` | 0/1 | Subject to carve-outs below |
| `messaging.*` | 0/1 | **Experimental** — enumerate individually |
| `code.*` | 0 | Subject to carve-outs below |
| `exception.*` | 0/1 | Subject to carve-outs below |
| `user_agent.*` | 0 | |
| Class 2 keys | 2 | Declared by policy packs, never by prefix |

## 🔒 Carve-outs — denied within allowed families

This table is the compliance argument. A family allow is broad by intent; these
are what make it safe. Rev 3 **D3.1**'s audit should treat this list as its
primary object rather than reviewing keys one at a time.

| Key | Class | Reason |
|---|---|---|
| `http.request.header.*` | 4 | Includes `Authorization`. **Opt-in upstream** — this carve-out prevents it being enabled later without review, rather than blocking current behaviour |
| `http.response.header.*` | 4 | Includes `Set-Cookie`. Same opt-in status |
| `url.full` | 3 | Carries identifiers where routes encode them — Rev 3 **D0.4b** |
| `url.query` | 3 | Same |
| `db.query.text` | 3 | **Nothing emits this** — the database is CouchDB, per [ADR-0023](./adr/0023-couchdb-changes-the-database-surface.md). Carved out anyway, costlessly, so a future SQL component cannot arrive unnoticed |
| `db.statement` | 3 | Pre-rename form of the above. Same reasoning |
| `db.query.parameter.*` | 3 | Same reasoning |
| `url.full`, `url.query` **on CouchDB spans** | 3 | 🔒 **The real database carve-out.** CouchDB is HTTP: `GET /{db}/{docid}` puts the document ID in the path and `_view?key=` puts the lookup key in the query string. QD2 answered 2026-08-11 — document IDs are opaque, not derived from applicant data — so this carve-out is now defense-in-depth rather than the compliance-blocking case it was drafted against |

`exception.message` remains allowed on application and HTTP spans, where it
carries diagnostic value and rarely quotes record contents. It is covered by
collector-side pattern scanning, which catches shapes it knows and will miss
novel ones — an accepted residual, recorded in ADR-0004.

## Class 2 — declared by policy packs

Never allowed by prefix. Each key is declared individually, with provenance, per
ADR-0017.

| Key | Pack | Status |
|---|---|---|
| `correlation.id` | mechanism | Workflow identity — [ADR-0007](./adr/0007-correlation-lifetime.md) |
| `session.id` | mechanism | Browser page load — ADR-0007 |
| `message.id` | mechanism | ADR-0007 |
| `causation.id` | mechanism | ADR-0007 |
| `tenant.id` | mechanism | **Withheld until D0.3** — [ADR-0011](./adr/0011-estate-vocabulary-versus-domain-vocabulary.md) |
| `application.id` | Kyc | Opaque applicant reference |

🔒 No Class 2 key may be a metric dimension. Rev 3 **D2.1** rule 1, enforced by
the analyzer.

## What is still missing

The empirical half of ADR-0018. Until the Phase 1 fixture runs and dumps every
key actually emitted, this document describes what we believe the instrumentation
produces — not what it does produce. Those differ, particularly in the contrib
packages Rev 3 **N-D1** notes ship as prerelease.

Reconciliation at Gate 2:

- [ ] Dump every attribute key emitted by the fixture on both runtimes
- [ ] Reconcile against the families above; classify anything unaccounted for
- [ ] Enumerate the `messaging.*` keys actually used, individually
- [ ] Set the pinned semconv version in code
- [ ] Confirm the dropped-key metric fires for a deliberately unlisted key
