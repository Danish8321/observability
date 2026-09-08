# Attribute allowlist

**Status:** families and carve-outs are **implemented and enforced at run time**
as of 2026-08-24 — `AttributeAllowlist` + `AllowlistProcessor` in the mechanism
package, wired into both the .NET 10 and 4.8 entry points as the last processor
before the exporter. The concrete key list is still **not empirically
validated** — that requires the Phase 1 fixture and remains a Gate 2 item per
[ADR-0018](./adr/0018-allowlist-composition.md).

🔒 **Provenance is live as of 2026-08-25.** Both packages are strong-named
(`raksawi.snk`, committed — a strong-name key is an identity marker, not a
secret). ADR-0017's check compares the declaring assembly's public key against
the mechanism assembly's own, at run time in `AttributeAllowlist` and at compile
time in the analyzer, so an assembly outside the closed set cannot declare an
allowlist key at either point. Before signing, both checks passed vacuously on
empty tokens and any assembly in the process could declare anything.

Both fail *open* when there is nothing to compare — an unsigned build accepts
every declaration rather than silently emptying the allowlist, which would be
the worse failure. So a build that loses its signing configuration loses
provenance quietly; `The_mechanism_assembly_is_strong_named` is the test that
catches it.

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
| `code.*` | 0 | Subject to carve-outs below |
| `exception.*` | 0/1 | Subject to carve-outs below |
| `user_agent.*` | 0 | |
| Class 2 keys | 2 | Declared by policy packs, never by prefix |
| `messaging.*` keys | 0/2 | **Not a family.** Enumerated individually below |

## 🔒 `messaging.*` — enumerated, not a family

The stability policy above says experimental attributes are enumerated
individually. Until 2026-09-08 this document said that while
`AllowlistRules.AllowedFamilies` and both collector keeps admitted the whole
`messaging.` prefix — the policy was stated and not implemented, so any
messaging key, including one invented by a service, reached the store on the
prefix alone.

This is the enumeration. It is **what the reference services actually emitted at
the sink**, dumped from `e2e-instrumented.sh` and reconciled key by key, rather
than read off the specification — the empirical half of
[ADR-0018](./adr/0018-allowlist-composition.md). Eight of the twelve are set by
the NATS client's own instrumentation and were named nowhere in this repository
before this pass.

| Key | Class | Source | Notes |
|---|---|---|---|
| `messaging.system` | 0 | sample + client | `nats` |
| `messaging.operation` | 0 | client | `publish` / `receive` |
| `messaging.destination.name` | 0 | sample + client | The subject. Structural in this estate — subjects are static names, not templates carrying identifiers |
| `messaging.destination.template` | 0 | client | |
| `messaging.destination.temporary` | 0 | client | |
| `messaging.destination_publish.name` | 0 | client | |
| `messaging.nats.message.subject` | 0 | client | NATS-specific spelling of the subject |
| `messaging.message.id` | **2** | sample + client | 🔒 Opaque per-message identifier. See the note below |
| `messaging.message.body.size` | 0 | client | |
| `messaging.message.envelope.size` | 0 | client | |
| `messaging.client_id` | 0 | client | |
| `messaging.attempt` | 0 | sample | **Not a semantic convention key.** The reference consumer sets it on a retry and dashboard 3.4 reads it; ADR-0025 requires a key we invent to be declared rather than admitted by prefix, so it is named here deliberately |
| `messaging.consumer.group.name` | 0 | *nothing yet* | Specified by [`diagnostic-queries.md`](./diagnostic-queries.md) as the dimension for every async pipeline panel. The sample uses core NATS with no consumer group, so nothing emits it. Admitted ahead of use so the allowlist is not the blocker when it appears — the precedent is `db.query.text`, carved out costlessly against a component that does not exist yet |

🔒 **`messaging.message.id` is the identifier that actually travels.** The
declared Class 2 key is `message.id` ([ADR-0007](./adr/0007-correlation-lifetime.md))
and **nothing emits it** — confirmed at the sink. The never-a-metric-dimension
rule named only the declared spelling, so it protected a key that does not exist
while the key that does exist was unprotected. Both spellings are now listed, on
spans and logs as Class 2, and refused as metric dimensions.

A key absent from this table is dropped. That is the point: the convention has
moved repeatedly, so a rename upstream surfaces as a dropped-key count and a
review rather than as telemetry that quietly changes shape.

## Allowed families — on a log record

The span list above, unchanged: Rev 3 **D2.1** permits Class 2 on spans *and*
logs, so a narrower log list would contradict the policy it implements.
Enforced in-process by `LogAllowlistProcessor` on .NET 10 and by the collector's
`log_statements` on both runtimes
([ADR-0028](./adr/0028-logs-are-enforced-in-process-on-net10-only.md)).

Two differences, both tightenings:

- 🔒 **The conditional `url.full`/`url.query` pair is unconditional here** —
  denied outright. Those keys survive on a CouchDB span only because
  `CouchDbUrlPolicy` rewrote the document identifier first, and nothing rewrites
  a URL a service wrote into a log. No precondition, no exemption.
- **A structured log property is an attribute key.** `LogInformation("Screened
  {ApplicationId}", id)` emits an attribute named `ApplicationId`, which matches
  no family and no declaration and is dropped. Log properties must be named in
  the estate vocabulary — `{application.id}` — or they do not survive export.
  The dropped-key metric carries a `telemetry.signal` dimension so this is
  visible rather than mysterious.

The log **body** is not filtered by key and cannot be; that is ADR-0004's
interpolation ban and the collector's pattern scan, not this list.

## Allowed families — on a resource

Narrower on purpose ([ADR-0026](./adr/0026-resource-attributes-are-allowlisted-narrowly.md)).
A resource says who is emitting, not what happened, so nothing request-scoped
belongs on one.

| Family | Class | Notes |
|---|---|---|
| `service.*`, `deployment.*`, `vcs.*`, `cicd.*` | 1 | Identity and provenance |
| `telemetry.sdk.*`, `telemetry.distro.*` | 0 | What produced the telemetry |
| `process.*`, `host.*`, `os.*`, `container.*`, `k8s.*` | 0 | Where it ran — subject to the carve-outs below |

🔒 **Absent deliberately:** `http.*`, `url.*`, `db.*`, `messaging.*`,
`server.*`, `client.*`, `network.*`, `code.*`, `exception.*`, `user_agent.*`.
Allowing a span family here would let a service move a request-scoped value
onto its resource and out of reach of the span rules — the same key,
unfiltered, on every signal it emits for the life of the process. A test
asserts each of these is absent from the resource keep.

No Class 2 key is declarable on a resource. Those are request- or
workflow-scoped by definition; a resource is process-scoped.

| Resource carve-out | Class | Reason |
|---|---|---|
| `process.command_line` | 3 | Connection strings and credentials are passed as arguments often enough that a command line is Class 3 by default |
| `process.command_args` | 3 | Array-valued form of the above |
| `process.owner` | 2 | Machine account. An operator identity, and not a diagnostic input |

This is enforced at the **collector only** — the one enforcement point that
reaches an agent-instrumented service, which builds its resource from
`OTEL_RESOURCE_ATTRIBUTES` and is the case this exists for. Our own services
get their resource from `ServiceIdentity` and cannot set an arbitrary key
through `AddRaksawiObservability()`.

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

**How the two `url.*` rows resolve in code.** They read as contradictory and are
not: `url.full`/`url.query` are the allowlist's one *conditional* pair. Denied
on every ordinary span; allowed only on spans whose `server.address` matches a
configured `CouchDbHosts` entry, by which point `CouchDbUrlPolicy` has already
replaced the document ID and view key with placeholders. On a CouchDB span the
redacted URL shape is the whole diagnostic value of the span, so dropping it
would discard the reason the span exists.

🔒 The two directions fail **opposite** ways, deliberately. `CouchDbUrlPolicy`'s
redaction fails *open* — an unconfigured host means the URL is never redacted.
The allowlist fails *closed* — an unconfigured host means `url.full` is dropped
entirely. So a host missing from `CouchDbHosts` costs diagnostic value and does
not leak.

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
| `message.id` | mechanism | ADR-0007. 🔒 **Emitted by nothing** — the identifier that travels is `messaging.message.id`, enumerated above and classified the same way |
| `causation.id` | mechanism | ADR-0007 |
| `tenant.id` | mechanism | **Withheld** — [ADR-0011](./adr/0011-estate-vocabulary-versus-domain-vocabulary.md). D0.3 closed by working position ([ADR-0024](./adr/0024-estate-inventory-by-working-position-not-sweep.md)) and the position is a uniformly-KYC estate today, so it stays undeclared and is therefore dropped at run time |
| `application.id` | Kyc | Opaque applicant reference |

🔒 No Class 2 key may be a metric dimension. Rev 3 **D2.1** rule 1, enforced by
the analyzer.

## Class 0 and 1 — declared by policy packs

A domain says more than identifiers. Outcomes, statuses, and the infrastructure
a call addressed are not covered by any semantic-convention family, so they are
declared the same way Class 2 keys are — individually, in the pack, matched
exactly and never by prefix.

Allowing a `screening.` family by prefix was considered and rejected: a family
allow would let any future key under that prefix through all three enforcement
points without review, which is the default-deny this document exists to
describe. The cost is a package release per key, which ADR-0017 treats as a
feature.

| Key | Class | Pack | Meaning |
|---|---|---|---|
| `screening.outcome` | 0 | Kyc | Result of a screening decision |
| `screening.provider` | 0 | Kyc | Which provider answered |
| `screening.abandoned` | 0 | Kyc | Work will not complete — the signal that separates "never submitted" from "submitted and silently never finished" |
| `screening.abandon_reason` | 0 | Kyc | Closed set of reasons |
| `application.found` | 0 | Kyc | Lookup outcome. Not an identifier — see below |
| `application.status` | 0 | Kyc | Closed set of statuses |
| `couchdb.database` | 1 | Kyc | Which database was addressed |
| `couchdb.conflict` | 1 | Kyc | Whether a write lost a revision race |

Note `application.found` and `application.status` share a prefix with the
Class 2 `application.id` and are not identifiers. That is precisely why declared
keys match exactly (ADR-0018): a prefix rule here would either leak the
identifier or drop the outcomes.

All eight were found by running the analyzer over the Screening reference
service on 2026-08-25. Every one had been emitted since before any allowlist
existed, and every one was being dropped before export.

## What is still missing

The empirical half of ADR-0018. Until the Phase 1 fixture runs and dumps every
key actually emitted, this document describes what we believe the instrumentation
produces — not what it does produce. Those differ, particularly in the contrib
packages Rev 3 **N-D1** notes ship as prerelease.

Reconciliation at Gate 2:

- [ ] Dump every attribute key emitted by the fixture on both runtimes
- [ ] Dump what a 4.8 agent actually puts on a **resource**; reconcile against
      the resource families. ADR-0026 settles that set; it does not validate it
- [ ] Reconcile against the families above; classify anything unaccounted for
- [x] Enumerate the `messaging.*` keys actually used, individually — done
      2026-09-08 from an `e2e-instrumented.sh` sink dump, twelve keys emitted
      plus `messaging.consumer.group.name` admitted ahead of use. Covers .NET 10
      and NATS only; a second dump is needed when another broker or runtime
      appears
- [ ] Set the pinned semconv version in code
- [ ] Confirm the dropped-key metric fires for a deliberately unlisted key
- [ ] Dump every attribute key emitted on a **log record** by the fixture, and
      reconcile the property names services actually write against the families
      above — the naming gap ADR-0028 records is a guess at scale until then
