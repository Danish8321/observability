# 25. Domain attributes are declared individually, not allowed as a family

Date: 2026-08-25

## Status

Accepted — extends [ADR-0018](./0018-allowlist-composition.md), and records the
first defect found by the build-time enforcement point of
[ADR-0003](./0003-runtime-allowlist-at-source.md).

## Context

The allowlist had two shapes and no third: families allowed by prefix
(`http.`, `db.`, `messaging.` — semantic conventions, Class 0 and 1), and
Class 2 identifiers declared individually by policy packs
([ADR-0017](./0017-allowlist-declared-as-assembly-attributes.md)).

A business domain says more than identifiers. Outcomes, statuses, and the
infrastructure a call addressed are neither, and no semantic convention covers
them. Nothing in the allowlist described how they get out.

This was not noticed while it was theoretical. It surfaced on 2026-08-25, the
first time the analyzer was run over the `samples/` reference service, as eight
diagnostics:

| Key | Class |
|---|---|
| `screening.outcome`, `screening.provider`, `screening.abandoned`, `screening.abandon_reason` | 0 |
| `application.found`, `application.status` | 0 |
| `couchdb.database`, `couchdb.conflict` | 1 |

Every one had been emitted since before any allowlist existed, and every one
was being dropped before export. `screening.abandoned` and
`screening.abandon_reason` are the reference service's whole point: they are
what separates *nothing was submitted* from *something was submitted and
silently never finished*. The API returns 202 either way.

## Decision

**Domain attributes of any data class are declared individually in the policy
pack**, by the same `[assembly: AllowedAttributeKey]` mechanism Class 2 keys
already use. The attribute has always taken a `DataClass`; nothing new is built.

Allowing a `screening.` family by prefix was considered and **rejected**. A
family allow lets any future key under that prefix through all three enforcement
points without anyone reading it, which is the default-deny the allowlist exists
to be. The cost — a package release per key — is the one ADR-0017 already
accepted deliberately, so that a vocabulary change goes through the same review
path as any other schema change (Rev 3 **D2.7**).

**Declared keys continue to match exactly and never as a prefix**, per ADR-0018.
This is load-bearing here rather than tidy: `application.found` and
`application.status` share a prefix with the Class 2 `application.id`. A prefix
rule would either leak the identifier or drop the outcomes.

## Consequences

- The allowlist now has three shapes: families by prefix, declared Class 2
  identifiers, and declared domain attributes. `docs/allowlist.md` gains a
  section for the third.
- A domain adding a span attribute now gets a build diagnostic rather than
  telemetry that disappears silently. That is the intended trade: the failure
  moves from an incident to a pull request.
- Policy packs grow. `Raksawi.Observability.Kyc` went from one declaration to
  nine. This is visible, reviewable weight, which is the point — an allowlist
  whose size nobody notices is one nobody is reading.
- **The reference implementation is not exempt from governance.** `samples/` and
  the policy pack itself now run the analyzer. Every defect above existed for as
  long as the sample did and was found within a minute of turning it on, which
  is the argument for the enforcement point stated better than the design
  documents managed.

## A second defect, recorded here because it has the same root

The build passing did **not** mean the keys were exported. `AttributeAllowlist`
scanned only *loaded* assemblies, and the runtime loads an assembly on first use
of one of its types. `AddRaksawiObservability` is called at `Program.cs` line 16,
before any policy-pack type is touched — so the pack contributed nothing at run
time, while the analyzer, which reads the compilation's references, reported the
code as correct.

Two enforcement points disagreeing, silently, in the direction that costs
diagnostics during an incident. The scan now walks the entry assembly's
transitive references (skipping the platform closure, which can never carry a
declaration and would otherwise be loaded on the startup path for nothing), so
the result depends on what the application references rather than on when the
call happens.

An assembly loaded later still cannot contribute. Plugin scenarios are out of
scope and the collector remains the net.
