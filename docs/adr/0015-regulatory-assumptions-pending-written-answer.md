# 15. Proceed on assumed-strictest regulatory constraints, named explicitly

Date: 2026-08-10

## Status

Accepted — provisional. Superseded in part when the Rev 3 **I0.1** written answer
arrives.

**2026-08-11 (danish):** sending the regulatory request ([`regulatory-request.md`](../regulatory-request.md))
is deferred past the demo — revisit only if a data-protection point actually
surfaces. Not a change to the assumptions below, just to when they get chased.

## Context

Rev 3 **I0.1** requires the regulatory position in writing before Gate 0: data
residency obligations, mandated retention periods, audit-log requirements, and
who signs off on PII handling. That answer does not yet exist.

Every compliance decision in this repository rests on assumptions about it.
[ADR-0003](./0003-runtime-allowlist-at-source.md) assumes source-side redaction
is required rather than optional.
[ADR-0004](./0004-free-text-telemetry-and-exceptions.md) accepts pattern
scanning of free text at the collector, which is only defensible if the
collector sits inside the compliant boundary. Rev 3 **I3.3**'s audit-pipeline
split assumes regulatory retention obligations exist that differ from the
operational TTL.

Waiting blocks I1.1–I1.3 and therefore Gate 1. Legal answers take weeks.

## Decision

Proceed on the strictest plausible reading, with the assumptions written down as
falsifiable statements:

1. All telemetry — traces, metrics, logs — remains within the country of
   operation. No component, managed or otherwise, sits outside it.
2. The collector and both bake-off candidate stores sit inside the regulated
   boundary. Free-text pattern scanning at the collector
   (ADR-0004) is permissible only under this assumption.
3. Operational telemetry is never the system of record for audit evidence. It
   may hold copies or references. This is already a Rev 3 **I3.3** invariant and
   is restated here because it is load-bearing for the assumption set.
4. Operational retention is 14 days hot, 90 days cold. Regulatory retention, if
   any, is served by the separate audit pipeline and not by these numbers.
5. A named data protection owner signs the **D3.1** audit before production.

Design so that relaxing any assumption is a configuration change, and tightening
one is never required.

## Consequences

- The asymmetry is what makes this safe. Assuming strict and being permitted to
  relax costs a configuration change. Assuming loose and being required to
  tighten costs host re-siting, possible re-ingestion, and — if regulated data
  has already landed somewhere non-compliant — a disclosure.
- Each assumption is written as a statement that can be shown false, so the
  arrival of the I0.1 answer is a review of five specific items rather than a
  re-reading of every ADR.
- 🔒 Assumption 5 is not an assumption the project can satisfy by itself. Rev 3
  **Gate 3** requires a signed PII audit, and a signature requires a person. No
  data protection owner is currently named. Until one is, Gate 3 cannot close
  regardless of how sound the redaction is. This is recorded as a blocking
  dependency rather than a technical risk.
- This ADR is provisional by construction. It is expected to be revised, not
  merely referenced, when I0.1 is answered.
