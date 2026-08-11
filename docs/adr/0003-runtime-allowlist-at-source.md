# 3. Source-side redaction is a runtime allowlist, not a deny-list

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **I3.2** specifies defense in depth — `source redaction → collector
redaction → storage RBAC → retention` — and states that the collector is the
net, not the primary control. Rev 3 **Gate 3** requires redaction verified by
inspecting stored data. If the only real control were at the collector, that
gate would be verifying the net.

So a source-side runtime control is required. The Roslyn analyzer
([ADR-0002](./0002-closed-allowlist-provenance.md)) cannot be that control: it
sees `SetTag` call sites in first-party code and nothing else. It cannot see
attribute keys composed from variables, attributes emitted by third-party
instrumentation packages, or values that arrive through HTTP and SQL
instrumentation. `url.query` capture is not a call site in our code at all.

Four source-side options were considered: build-time enforcement only, a
runtime deny-list of known-bad keys, a runtime allowlist, and value-pattern
scanning.

## Decision

Span and log attributes pass through a runtime **allowlist** before export. An
attribute whose key is not allowlisted is dropped at source.

Class 0 and Class 1 keys (infrastructure counters, `trace_id`, `span_id`,
`service.*`) and resource attributes are allowlisted as families rather than
enumerated key by key. Class 2 keys are enumerated by the policy packs.

Dropped keys are counted as a metric, dimensioned by attribute key.

Value-pattern scanning — regex matching CPR, MRZ, or email shapes against every
attribute value — is explicitly **not** done at source. It is available at the
collector.

## Consequences

- This is what Rev 3 **D2.1**'s default-deny classification actually means when
  expressed in code. A deny-list would only catch key names we predicted;
  an attribute named `applicantIdentifier` carrying a CPR would pass a deny-list
  and is stopped by an allowlist.
- Runtime cost is one hash-set lookup per attribute — nanoseconds, invisible
  against span export cost, and no risk to the **D3.3** overhead tripwire.
  Pattern scanning was rejected precisely here: regex over every value on every
  span is unbounded work on the request path, and Rev 3 **I3.6** holds that
  telemetry must never participate in business request success or failure.
  Pattern matching costs the platform's CPU at the collector instead.
- Legitimate attributes from newly added instrumentation packages will silently
  disappear until allowlisted. The dropped-key metric exists so that this is
  answerable from a dashboard rather than by reading library source — without
  it, the failure mode is indistinguishable from instrumentation not working.
- 🔒 Source redaction being a real control, not a formality, is what allows the
  Gate 3 check on stored data to be evidence rather than a tautology.
