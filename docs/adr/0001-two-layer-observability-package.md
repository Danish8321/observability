# 1. Two-layer observability package: mechanism and policy

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D2.4** specifies `Company.Observability` as one multi-targeted package
(`net48;net10.0`), with all governance — resource schema, attribute allowlist,
redaction rules, cardinality policy — in the shared compilation. The guarantee
is that rules are identical across runtimes *by construction*, because they are
the same source compiled twice.

That guarantee is about **runtime** uniformity. It does not speak to a second
axis that became visible when we asked whether the package could be integrated
into any .NET 10 or .NET Framework application.

The library as described mixes two kinds of content:

- **Mechanism** — resource schema and fail-fast validation (D2.5), `http/protobuf`
  enforcement (D1.3b), W3C `Activity` format forcing (D1.3a), exporter safety
  including batch-only processors and bounded timeouts (I3.6), sampler defaults,
  instrumentation registration, and the Roslyn analyzer's diagnostic machinery.
- **Policy** — the Class 2 opaque-identifier list, the Class 3/4 redaction rules
  (CPR, passport, MRZ), the attribute allowlist, and the governed helpers
  `AddApplicantContext` / `RecordScreeningDecision` (D2.6).

Mechanism is reusable by any .NET service in the estate. Policy encodes a
specific regulated KYC domain and is meaningless outside it.

## Decision

Split along the mechanism/policy seam, not along the runtime seam:

- `Company.Observability.Core` — mechanism. Multi-targeted `net48;net10.0`.
  Depends on nothing domain-specific. Integrable into any .NET 10 or .NET
  Framework 4.8 service in the estate.
- `Company.Observability.Kyc` — policy. Multi-targeted `net48;net10.0`.
  Attribute allowlist, redaction rules, governed helpers, Class 2 identifier
  definitions. Depends on Core.

Both layers keep D2.4's original guarantee: each is one multi-targeted package
with its governance in the shared compilation, so no rule can differ between
`net48` and `net10.0`.

Policy remains **compiled**, not configuration. A config-driven allowlist was
considered and rejected: per-service configuration is editable per service,
which reintroduces exactly the drift D2.4 exists to prevent.

## Consequences

- Non-KYC services in the estate can adopt Core without inheriting KYC
  vocabulary they have no use for.
- One additional package boundary to version and publish. D2.7's independent
  versioning now applies to two artifacts; a policy change no longer forces a
  mechanism release, and vice versa.
- D2.4 warns against splitting packages below roughly ten Framework consumers.
  That warning targets a *runtime* split (`netstandard2.0` + per-runtime
  packages), which multiplies packaging and test surface without changing what
  is guaranteed. This split is orthogonal and does not multiply runtime targets.
- The analyzer must read its allowlist from the policy layer while shipping its
  diagnostics from the mechanism layer. That seam needs its own decision.
- 🔒 Compliance framing changes: a PII audit (D3.1) can now scope the redaction
  rules to a single package rather than to the whole library surface.
