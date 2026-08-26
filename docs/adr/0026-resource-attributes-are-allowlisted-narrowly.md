# 26. Resource attributes are allowlisted, and more narrowly than span attributes

Date: 2026-08-26

## Status

Accepted. Extends [ADR-0018](./0018-allowlist-composition.md) and closes a gap
left open by [ADR-0009](./0009-governing-agent-instrumented-services.md).

## Context

Until now the collector filtered span attributes and metric datapoint
attributes, and nothing else. Resource attributes passed through untouched.

For our own services that was defensible: the resource is built by
`ServiceIdentity.BuildResource`, in-process, from options the library controls.
An agent-instrumented .NET Framework service is the case the gap actually
mattered for. It contains none of our code, and its resource comes from
`OTEL_RESOURCE_ATTRIBUTES` — an environment variable set per application pool
by whoever configured the host. Nothing constrained it at any of the three
enforcement points.

A resource attribute is also the worst place for an unfiltered key to land. It
is attached to **every span, metric, and log** the service emits, for the life
of the process, and it is stored on each of them. A leak on one span is one
row; a leak on a resource is the entire retention window.

The gap was recorded in the collector config and in this repository's README
rather than papered over, on the stated basis that closing it needed the
resource families settled at Gate 2. That deferral is no longer worth its cost:
the families can be settled now from what actually populates a resource, and
`e2e.sh` can prove the result on received telemetry today.

## Decision

The collector allowlists resource attributes, on both the traces and metrics
pipelines, with the same deny-then-keep shape used for spans.

The allowed resource families are **narrower than the span families**:

```
service.  deployment.  vcs.  cicd.
telemetry.sdk.  telemetry.distro.  process.  host.  os.  container.  k8s.
```

Absent, deliberately: `http.`, `url.`, `db.`, `messaging.`, `server.`,
`client.`, `network.`, `code.`, `exception.`, `user_agent.`. A resource says
who is emitting, not what happened.

Three carve-outs within the allowed families: `process.command_line`,
`process.command_args` (connection strings and credentials are passed as
arguments often enough that a command line is Class 3 by default) and
`process.owner` (a machine account is an operator identity and is not a
diagnostic input).

No Class 2 key is declarable on a resource. `correlation.id` and the rest are
request-scoped or workflow-scoped by definition; a resource is process-scoped.

## Consequences

- 🔒 **The narrowness is the control, not an oversight.** If the span families
  were allowed here, a service could move a request-scoped value onto its
  resource and escape the span rules entirely — the same key, unfiltered, on
  every signal it emits. A test asserts each span-only family is absent from
  the resource keep.
- The rule is stated twice in the collector config, because the transform
  processor scopes statements per signal. `CollectorAllowlistContractTests`
  compares the two copies and fails if they drift.
- `e2e.sh` asserts it on received telemetry: `service.name` and `host.name`
  survive, `process.command_line` and a hand-rolled `operatorEmail` do not, on
  both the traces and the metrics pipeline.
- A host whose `OTEL_RESOURCE_ATTRIBUTES` sets something outside these families
  will silently lose it. That is default deny working, and it is the same
  bargain the span allowlist already makes. The dropped-key metric at Gate 2 is
  what makes such a loss visible rather than mysterious.
- This is enforced at the **collector only**. There is no analyzer rule and no
  in-process resource filter: our own services cannot set an arbitrary resource
  key through `AddRaksawiObservability()` in the first place, so a second
  enforcement point would guard a path that does not exist.
- Gate 2 still owes the empirical half of ADR-0018 for resources as for spans —
  dump what a 4.8 agent actually puts on a resource and reconcile it against
  the families above. Settling the families is not the same as validating them.
