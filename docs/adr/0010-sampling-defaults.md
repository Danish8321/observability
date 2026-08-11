# 10. Absent sampler configuration is a production boot failure

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **Appendix A** makes `OTEL_TRACES_SAMPLER` and `OTEL_TRACES_SAMPLER_ARG`
platform-owned. Rev 3 **I3.5** sets the initial production value at head
sampling 1.0, on the reasoning that tail sampling can only decide on traces it
actually receives: at head 0.5, a tail policy promising to keep 100% of error
traces keeps 100% of the half that survived.

Neither says what the library does when those variables are absent — local
development, a newly created service, or an application pool whose environment
variables did not apply, which Rev 3 **F-I2** flags as an easy mistake on IIS.

## Decision

Absent sampler configuration defaults to 1.0 in development and **fails to boot
in production**.

## Consequences

- This mirrors Rev 3 **D2.5**'s treatment of deployment metadata: warn locally,
  refuse in production. A developer running without a platform configuration
  gets a working default; a production process without one does not start.
- The reasoning is that a missing sampler variable is not an isolated mistake.
  It indicates the platform's configuration did not reach the process at all,
  which is the same fault that would leave `OTEL_EXPORTER_OTLP_ENDPOINT` unset.
  Absorbing it silently converts a configuration failure into a capacity problem
  attributed to the wrong cause.
- Defaulting to a reduced rate was rejected outright. It contradicts **I3.5**:
  a service quietly sampling at 0.1 loses error traces invisibly, which is
  precisely the failure the plan exists to prevent.
- This extends the boot-refusal set beyond what **D2.5** enumerates.
  [ADR-0005](./0005-enforcing-the-framework-wiring.md) declined to refuse boot
  for a wrong `Activity` ID format, and the distinction is deliberate: a wrong
  trace format is one specific mistake in an otherwise-configured service, while
  an absent sampler indicates configuration is missing wholesale. If that
  distinction is ever judged too fine, the consistent alternative is 1.0 with a
  loud warning and a metric, not a reduced default.
- The platform keeps its **Appendix C** rollback lever: changing
  `OTEL_TRACES_SAMPLER_ARG` and restarting still works, because the library only
  supplies a default when the variable is absent.
