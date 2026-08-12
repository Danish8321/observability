# 11. Tenancy is estate vocabulary; screening is domain vocabulary

Date: 2026-08-10

## Status

Accepted

## Context

[ADR-0001](./0001-two-layer-observability-package.md) split mechanism from
policy and made multiple policy packs possible. It did not say which packs exist
at v1, which leaves open whether that seam is load-bearing or speculative.

Rev 3 **D2.1** lists the Class 2 opaque business identifiers together:
`tenant.id`, `application.id`, `correlation.id`, `message.id`, `causation.id`.
Grouping them by data class is correct for the classification table and says
nothing about which package should own each key.

`correlation.id`, `message.id`, and `causation.id` are plainly estate-wide —
they are the correlation model from
[ADR-0007](./0007-correlation-lifetime.md), which no domain owns. `tenant.id` is
the ambiguous one: it appears alongside KYC identifiers but means nothing
KYC-specific. `application.id` and screening decisions are unambiguously KYC.

## Decision

At v1 there is one policy pack, `Acme.Observability.Kyc`, holding
`application.id`, the screening helpers, and the CPR / passport / MRZ redaction
rules.

`tenant.id` is promoted into the mechanism package alongside `service.*` and the
correlation identifiers, as estate vocabulary.

The test for promotion: a key belongs in mechanism when it is meaningful
independently of any domain, and in a policy pack when its meaning depends on
one.

## Consequences

- A non-KYC service can express tenancy without taking a dependency on KYC
  vocabulary. Leaving `tenant.id` in the KYC pack would have forced exactly that,
  which contradicts what ADR-0001 was for.
- The mechanism/policy seam has a real consumer at v1 rather than being
  speculative, so it is exercised before more packs exist.
- Promotion is hard to reverse. A key shipped in mechanism is referenced
  estate-wide, and demoting it later into a pack breaks every service that used
  it. The promotion test above exists so that the judgement is made explicitly
  rather than by whoever adds the next key.
- 🔒 This assumes the estate is mixed. If all services in scope are KYC,
  promoting `tenant.id` is speculative generality and a single pack would have
  been the honest choice.

## Dependency on D0.3

The assumption above is confirmed or refuted by Rev 3 **D0.3**'s estate
inventory, which is a Phase 0 item. Per
[ADR-0012](./0012-net10-first-sequencing.md) no code is written in this
repository until after Gate 1, so the inventory lands before anything is
compiled, published, or consumed.

There is therefore no window in which the assumption can cause damage: the
irreversibility described above only applies once the key ships to consumers.

To keep that guarantee explicit rather than incidental: **`tenant.id` is not
declared in either package until D0.3 confirms the estate is mixed.** If the
inventory shows a uniformly KYC estate, this ADR is revised before its first
line of code exists.

**Working position, 2026-08-12 (danish):** today the estate is uniformly KYC —
the only services running are KYC, and only they use NATS. The stated plan is
to onboard other services, KYC and non-KYC alike, once the KYC path is
proven. This is a direction, not the D0.3 inventory itself — D0.3 still has
not run (no service catalogue, no reconciled five-source sweep). Promotion of
`tenant.id` stays justified against the *planned* mixed estate rather than
today's actual one; if that plan changes before D0.3 runs, this position
needs revisiting before any code depends on it.
