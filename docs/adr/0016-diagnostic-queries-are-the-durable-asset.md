# 16. Diagnostic queries are specified store-neutrally; dashboards are built once

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **I0.6** and **I3.11** require the telemetry store to be chosen from
bake-off evidence on real traffic rather than by scorecard, with the deciding
question being how many systems the team operates at 3am. That decision has not
been made.

Rev 3 **I4.3** places four dashboards in scope — service golden signals, trace
explorer, async pipeline health, stack health — and **I3.9** adds the stack
health dashboard as a Phase 3 deliverable. Grafana dashboards are Grafana JSON;
SigNoz has its own model. They do not port between the two.

This creates an ordering problem: the stack health dashboard is needed *during*
the bake-off, because it is how the bake-off is observed, yet it is built in the
tool the bake-off is meant to select.

Building every dashboard twice was considered. It would produce a genuinely
comparable evaluation — which tool is pleasant to build in is half the 3am
question — and it doubles the work at roughly 0.25 FTE, discarding half.

## Decision

The durable asset is the diagnostic question, not its serialised form.

Each dashboard panel is specified in this repository as a store-neutral
statement of what it answers and what it is dimensioned by — "oldest unprocessed
message age by consumer group", "p99 latency by route where the trace contains
an error", "services reporting divided by services expected".

Dashboards are then built once, in whichever store is being evaluated, and
rebuilt from the same specifications if the other store wins.

## Consequences

- The bake-off can proceed with real dashboards without committing the dashboard
  work to the losing store.
- A rebuild after I3.11 is real work, but it is transcription from an existing
  specification rather than rediscovery of what the panels were for.
- Specifications drift from what is deployed unless something checks. Nothing
  currently checks. This is accepted for now and should be revisited when the
  store is chosen and the dashboards are under version control.
- The specifications also serve Rev 3 **D4.5**, which requires runbook entries
  naming the exact diagnostic query per failure mode. Those queries and these
  panel specifications are the same artifact viewed from two directions.
- **Resolved 2026-08-10.** Rev 3 **I2.5** notes SigNoz Community gates SSO
  entirely while Grafana OSS supports OIDC, which raised the possibility that the
  store decision was already made on that axis and the bake-off was ceremony. SSO
  is confirmed **not mandatory**, so both candidates remain in contention and the
  bake-off is a real comparison. This decision would have held either way, since
  the specifications survive the choice.
