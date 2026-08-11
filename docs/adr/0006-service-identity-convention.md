# 6. Service identity: bare name plus namespace

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D2.4b** requires `service.name` conventions to be settled before the
first service ships, on the grounds that greenfield is the only moment it is
free: every dashboard, alert rule, saved query, SLO, and runbook keys off it,
and renaming later breaks all of them at once.

Rev 3 **Appendix D** splits ownership — the application owns the value, the
platform governs it. Rev 3 **D2.5** makes the library fail to boot when
`service.name` is absent, so the library owns the rule. **Appendix B** already
lists `service.namespace` as platform-owned, defaulted by the library per
domain.

Four shapes were considered: a bare name; a bare name plus namespace; a dotted
compound name carrying the domain; and a name mirroring the deployment unit.

## Decision

`service.name` is a bare kebab-case name — `screening-api`, `kyc-portal`.
`service.namespace` carries the domain — `kyc`.

`service.name` is stable for the life of the service. It is never derived from
anything the deployment owns: not the pod name, not the IIS application pool,
not the Windows service name.

The convention binds agent-instrumented services identically, even though no
library validates them.

## Consequences

- Collisions across domains become impossible without lengthening names, and
  `service.namespace` is available free as a dashboard dimension and an RBAC
  boundary.
- A dotted compound name was rejected because it duplicates
  `service.namespace`. OpenTelemetry semantic conventions treat the two as
  distinct fields, so the prefix would either disagree with the namespace or
  leave it unset, and tooling would surface both.
- Mirroring the deployment unit was rejected because it couples telemetry
  identity to infrastructure naming. An app-pool rename would then silently
  split a service's history in two — the same class of breakage D2.4b exists to
  prevent, arriving through infrastructure rather than through a decision.
- Two attributes must be correct rather than one, and queries must filter on
  both to be unambiguous.
- 🔒 **Open gap.** Agent-instrumented 4.8 services (Rev 3 **D2.9**) set
  `OTEL_SERVICE_NAME` from the environment with no library in the process, so
  **D2.5**'s fail-fast validation does not apply to them. Their resource
  attributes are unvalidated by construction. Detection has to happen outside
  the process — reconciling reporting services against expected services, which
  is the **I4.6** coverage SLI. This ADR does not close that gap; it names it.
