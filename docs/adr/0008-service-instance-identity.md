# 8. Service instance identity: supplied if known, derived if not

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **Appendix B** states the semantics of `service.instance.id` but not its
formula. A fresh GUID per process start is acceptable for ephemeral containers
and poor for IIS and Windows services, where every app-pool recycle would invent
a new instance and the attribute would stop meaning anything to an operator. The
attribute must answer "which box is this?" and keep answering it across
restarts.

The durable identity available differs by host: a pod name in Kubernetes,
machine plus application pool on IIS, machine plus service name for a Windows
service, machine plus assembly for Kestrel on a VM. The estate contains all four.

A single uniform hash of machine plus entry assembly was considered and
rejected: two application pools running the same assembly on one host — plausible
in the 4.8 estate — would collide into one instance.

## Decision

`OTEL_SERVICE_INSTANCE_ID`, when supplied by deployment, is used verbatim.

When it is absent, the library derives one from the host's durable identity: pod
name where present; machine name plus application pool on IIS; machine name plus
service name for a Windows service; machine name plus entry assembly otherwise.

A recycle or restart does not change the value. Process starts are observable as
a metric instead.

## Consequences

- The failure mode is degradation rather than collision or absence. Pure
  derivation can silently produce a colliding identity; pure deployment supply
  produces nothing at all when unset, and Rev 3 **F-I2** already establishes
  that IIS environment variables are easy to get wrong. Neither failure is
  visible in telemetry that otherwise looks healthy.
- This matches the ownership split the rest of the schema already uses: the
  library owns the schema and its defaults per **D2.5**, deployment owns
  environment-specific values per **Appendix D**.
- Host detection is the risk this decision accepts. Misdetection yields a
  plausible-looking wrong identifier that nobody notices, so the derivation must
  record which strategy it chose as an attribute, making the choice inspectable
  rather than implicit.
- Changing the derivation later splits an instance's history in two, so the
  formula is treated as schema and versioned under **D2.7**.
