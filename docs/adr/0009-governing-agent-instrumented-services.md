# 9. Agent-instrumented services are governed at the collector, fail-closed

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D2.9** defaults most of the .NET Framework 4.8 estate to the zero-code
auto-instrumentation agent: no recompile, and `http/protobuf` by default. Those
processes contain no library, so none of the controls decided so far reach them —
no resource validation (D2.5), no runtime allowlist
([ADR-0003](./0003-runtime-allowlist-at-source.md)), no analyzer
([ADR-0002](./0002-closed-allowlist-provenance.md)), no governed helpers, no W3C
self-check ([ADR-0005](./0005-enforcing-the-framework-wiring.md)).

Rev 3 argues the PII surface is smaller by construction, because an agent cannot
attach business attributes at all, so the audit narrows to URL capture, SQL
statement capture, and log content. That is true and it is not the same as being
governed. Rev 3 **D2.4** states the principle that decides this: a rule binding
some services and not others is not a weaker rule, it is a gap with a specific
list of service names in it.

## Decision

Agent-instrumented services are enumerated in this repository, each with its
allowlist, and that policy is enforced in a dedicated collector pipeline for
those services. The enumeration is PR-gated exactly as policy packs are.

The pipeline is **fail-closed**: telemetry from a service not enumerated here is
not exported.

A test asserts that the collector policy for a service and the equivalent
manifest express the same allowlist, so the two enforcement points cannot drift.

## Consequences

- Agent services and SDK services reach the same outcome through different
  enforcement points. The closed-set property from ADR-0002 is preserved: the
  set of things that may be said is defined in one repository and nowhere else.
- For this subset, the collector is the primary control rather than the net,
  contradicting Rev 3 **I3.2** as written. This is the second such compromise
  after [ADR-0004](./0004-free-text-telemetry-and-exceptions.md), and it is
  accepted for the same reason: there is no source-side control available in a
  process that contains none of our code.
- Fail-closed converts a silent compliance gap into a visible coverage gap. An
  ungoverned service currently looks identical to a healthy one; under this
  decision it stops reporting, and Rev 3 **I4.6** already makes coverage —
  services reporting divided by services expected — an SLI with a 100% target.
  The detector therefore already exists and needs no new alert. **Amended
  2026-08-10:** that reasoning assumed a source for *services expected*. None
  existed — see
  [ADR-0021](./0021-service-register-is-the-coverage-denominator.md), which
  creates the register the detector depends on. Until D0.3 produces it, this
  decision has no detector at all.
- Two enforcement mechanisms must be kept aligned. The drift test is what makes
  that maintainable; without it this decision would recreate the drift ADR-0002
  exists to prevent.
- The **D2.9** capture-defaults review remains required before a service is
  enumerated. This decision governs what leaves the collector, not what the
  agent collects.
