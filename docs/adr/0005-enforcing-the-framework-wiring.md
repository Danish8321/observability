# 5. The .NET Framework wiring is enforced, not documented

Date: 2026-08-10

## Status

Accepted

## Context

The .NET Framework 4.8 implementation guide opens by naming three failure modes
and stating that all three fail silently: telemetry arrives, dashboards
populate, and traces are broken.

| Failure | Symptom |
|---|---|
| W3C trace context not forced (F-D2) | Every cross-runtime trace splits in two |
| gRPC exporter (F-D3) | No spans at all, no error surfaced |
| Provider not disposed (F-D5) | Final batch lost on every app-pool recycle |

None of the three can be fixed from inside the library. W3C forcing must be the
first statement in `Application_Start`, before any `Activity` exists. Disposal
must happen in `Application_End`. Both are host responsibilities.

Rev 3 **F-I5** notes these are the highest-risk services in the estate despite
being the fewest — the failure modes are silent and the code is the least
familiar. Relying on a human to remember a checklist item, on a codebase touched
once a year, is the weakest available control at exactly the point where the
consequences are least visible.

## Decision

Enforce at build where the wiring is visible, and detect at run where it is not.

**Build time**, analyzer rules on the `net48` target:

- Error if `CompanyObservability.Start()` is called without
  `Activity.ForceDefaultIdFormat` being set earlier in the same method.
- Error if the returned `IDisposable` is not stored in a field that is disposed
  in `Application_End`.
- Error if the OTLP protocol is set in code to anything other than
  `http/protobuf`.

**Run time**, at `Start()`: inspect `Activity.DefaultIdFormat`. If it is not
W3C, emit a loud warning and increment a metric. Do **not** throw.

## Consequences

- Two of the three silent failures become build failures, on the runtime where
  silent failure is most likely to survive to production.
- The runtime check covers the case the analyzer structurally cannot see: W3C
  set through `DOTNET_SYSTEM_DIAGNOSTICS_ACTIVITY_DEFAULTIDFORMAT` rather than
  in code, which is the documented path for services whose code cannot be
  touched.
- The runtime check warns rather than throws, deliberately. Rev 3 **D2.5** makes
  refusing to boot correct for identity — a service that cannot name itself is
  useless — but a service with a broken trace format is still processing KYC
  applications correctly. Killing it to protect telemetry would invert the
  **I3.6** invariant that telemetry must never participate in business request
  success or failure.
- The warning is paired with a metric because a warning in a log on an IIS host
  is not a control. The metric surfaces on the stack health dashboard (I3.9),
  next to coverage.
- The gRPC failure mode is only partly addressed. When the protocol is set by
  environment variable the exporter fails without surfacing, and neither the
  analyzer nor a startup check reliably observes it. It remains a Gate 1
  verification item, checked by observing spans rather than by inspecting
  configuration.

## These rules are specified, not observed

The three failure modes above are taken from the .NET Framework 4.8
implementation guide. Nobody on this project has yet observed any of them.
The rules encode what the guide states happens, and
[ADR-0012](./0012-net10-first-sequencing.md) defers 4.8 runtime validation to
Phase 2, so Phase 2 is the first point at which they are confirmed.

To avoid committing to unverified behaviour: the `net48` analyzer rules are
**not implemented until Phase 2 validation observes the failure modes**. This
costs nothing, because ADR-0012 sequences .NET 10 first and no 4.8 consumer
exists before Phase 2. The rules are specified here and built once there is
evidence they are the right rules.

If Phase 2 observation contradicts the guide, this ADR is what gets revised —
before any rule is written against it.
