# Context

Ubiquitous language for the observability platform. Glossary only — no
implementation detail, no plans, no decisions. Decisions live in
[`docs/adr/`](./docs/adr/). Scope and sequencing live in [`README.md`](./README.md).

## Layers

**Core** — the package that knows how telemetry is produced and shipped, and
nothing about any business domain or any specific broker or database. Reusable
by any service, in this estate or outside it.
_Avoid_: mechanism layer

**Integration package** — knowledge of one technology that core deliberately
leaves out, such as a message broker or a database. A service references only
the integrations it actually uses.

**Compliance add-on** — the package that decides what a service may export:
the allowlist, data classes and their enforcement. Optional for core, required
for any service handling regulated data.
_Avoid_: policy layer

**Vocabulary pack** — one domain's declared attribute keys and governed
helpers, scoped to that domain. Depends on the compliance add-on.
_Avoid_: policy pack

**Mechanism and policy** — the distinction the packages are split along:
mechanism knows how telemetry works, policy knows what a domain may say. A
principle, not the name of any package.

## Governance

**Allowlist** — the set of attribute keys a service may attach to spans and
logs. Anything absent from it is dropped, not flagged. Default deny.

**Family** — a prefix allowed wholesale, such as `http.*`. The allowlist is
expressed as families rather than individual keys, so that code paths nobody
exercised are still covered.

**Carve-out** — a key or subtree denied inside an otherwise allowed family.
Carve-outs are where the compliance argument actually lives, because a family
allow is broad by intent.

**Provenance** — the property that makes an allowlist declaration trustworthy:
it came from a policy pack published by this repository, established by the
declaring assembly's strong name. A declaration without provenance is ignored.
The allowlist source set is closed.

**Governed helper** — a named method that attaches an approved attribute, so the
allowlisted key appears in one reviewed place rather than at every call site.
Not a wall: raw span tagging stays available, and the analyzer's complaint about
it is the trigger for review.

**Data class** — one of the five bands, 0 to 4, that decide where a piece of
data may appear. 0 infrastructure, 1 technical identifiers, 2 opaque business
identifiers, 3 restricted PII, 4 secrets. Classes 3 and 4 appear nowhere.
Class 2 appears on traces and logs and never as a metric dimension.

**Enforcement point** — one of the three places a rule is applied: the analyzer
at build, the library before export, the collector before storage. A service
instrumented by the zero-code agent has only the third.

## Correlation

**Trace** — one chain of hops, identified by `trace_id`. Technical, and may
legitimately terminate at a queue.

**Session** — one browser page load, identified by `session.id` and carried on
the wire as `X-Session-Id` (ADR-0041). Exists so that the AJAX calls of one page are
queryable together. Not a business fact.

**Correlation** — one business workflow, identified by `correlation.id`. Minted
by a service when a workflow begins. Survives across traces and does not depend
on sampling.

**Message** — one message on the wire, identified by `message.id`. Unchanged by
redelivery; that is what makes redelivery detectable.

**Causation** — the direct parent message of a message, identified by
`causation.id`. Distinct from correlation: correlation says which workflow,
causation says which message caused this one.

## Coverage

**Coverage** — services reporting divided by services expected. An observability
SLI in its own right; a shortfall is an incident even while every component
reports healthy.

**Freshness** — elapsed time from collector ingress to queryable. Distinguishes
"telemetry is thirty seconds behind" from "telemetry is twenty minutes behind",
which are different incidents.

**Fail-closed** — the property that anything the collector's allowlist does
not name is dropped before storage, so a governance gap shows up as missing
data and never as stored restricted data. A property of the collector, which
is the one enforcement point every service passes through.

**Required governance** — a service's declaration that it handles regulated
data. It turns the absence of in-process controls from a silent gap into a
failure to start.
