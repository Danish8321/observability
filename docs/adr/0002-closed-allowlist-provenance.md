# 2. Allowlist sources are a closed set

Date: 2026-08-10

## Status

Accepted. The manifest mechanism described below is superseded by
[ADR-0017](./0017-allowlist-declared-as-assembly-attributes.md), which replaces
`AdditionalFiles` manifests with assembly-level attribute declarations. The
closed-set decision — that allowlist sources are defined in this repository and
nowhere else — stands unchanged.

## Context

[ADR-0001](./0001-two-layer-observability-package.md) split the library into a
mechanism layer (`Core`) and a policy layer (`Kyc`), and left open how the
Roslyn analyzer learns its attribute allowlist.

The chosen mechanism is an `AdditionalFiles` manifest: the analyzer ships in
`Core` with the diagnostics and rule engine; each policy package ships a
manifest that MSBuild wires into the compilation. The analyzer unions every
manifest it is given.

This makes multiple domain policy packages possible — `.Kyc`, `.Payments`,
`.Onboarding` — each with its own allowlist and governed helpers, each service
referencing the packs its domain needs.

`AdditionalFiles` has no notion of where a file came from. A service team can
add one line to its own project file and extend the allowlist locally: no pull
request to this repository, no review, no data-protection sign-off. The
analyzer would accept it.

That is the same drift a configuration-driven allowlist would have introduced —
rejected in ADR-0001 — arriving through a different door.

## Decision

The set of allowlist sources is **closed**. The analyzer honours manifests
carried by policy packages published from this repository and rejects any other
manifest with its own diagnostic. Provenance is established by package
identity, not by filename.

Adding a domain policy package, or adding a key to an existing one, is a pull
request against this repository, reviewed under Rev 3 **Appendix D**:
application proposes, platform approves.

Raw `Activity` and `SetTag` remain available for genuine one-offs, per Rev 3
**D2.6** — a too-narrow abstraction gets bypassed, and a bypassed abstraction is
worse than none. The analyzer flags non-allowlisted keys, and that flag is the
trigger for review rather than a wall.

## Consequences

- 🔒 The quarterly cardinality and PII audits (Rev 3 Phase 5) become a question
  answerable from this repository's git history: what changed in the manifests
  since the last review. Under an open set, the same audit requires an
  estate-wide sweep of every service repository — which at ~0.25 FTE means it
  would not happen.
- Adding an attribute now has pull-request latency. This is intended: Rev 3
  **D2.1** requires a metric dimension and a baggage key to receive the same
  review, and an allowlist entry is the same class of change.
- Reversing this later is expensive in one direction only. Opening a closed set
  is harmless; closing an open one breaks builds across the estate at once.
- The provenance check is per-compilation, not per-request. It has no runtime
  cost and cannot affect the **D3.3** overhead tripwire.
- The analyzer must ship with a baseline rule set that works with no policy
  package present, so `Core`-only services still get compile-time enforcement of
  the runtime-agnostic rules (direct `AddOtlpExporter` outside the library,
  Class 2 identifiers as metric dimensions).
