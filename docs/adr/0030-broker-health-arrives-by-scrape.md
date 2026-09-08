# 30. Broker health arrives by scrape, in its own pipeline

Date: 2026-09-08

## Status

Accepted — extends [ADR-0009](./0009-governing-agent-instrumented-services.md),
first non-OTLP ingestion path

## Context

[ADR-0009](./0009-governing-agent-instrumented-services.md) is written about
services that contain none of our code but are still *ours* — agent-instrumented
.NET Framework processes we can configure. NATS is a step further out: it is a
third-party broker, it will never run our library, and there is no agent to
attach to it. Nothing we decide about instrumentation reaches it.

That matters because dashboard 3 (`docs/diagnostic-queries.md`) exists for
Rev 3 **D3.6** — the system showing perfect HTTP latency, zero errors and 30%
CPU while screening runs twenty minutes behind. Every signal that would show
that is broker-side. A pipeline that only accepts OTLP is structurally unable to
see the failure the dashboard was specified to catch.

Two things stood in the way, and the work list in
`docs/onboarding/backend-and-dashboards.md` had both wrong:

1. It said "`prometheus` receiver on NATS `:8222`". NATS `:8222` serves **JSON**
   (`/varz`, `/connz`, `/jsz`), not prometheus text, and collector-contrib has
   no NATS receiver. There is no configuration of the collector alone that can
   read it.
2. It said this unblocks panel 3.2, consumer lag. It does not. The reference
   services use **core NATS** (`ScreeningConsumer.cs`, `nats.SubscribeAsync`),
   which has no stream, no durable consumer and therefore no backlog to measure.
   A scrape against the running demo returns `jetstream_server_total_streams 0`
   and `jetstream_server_total_consumers 0`, and emits no
   `jetstream_consumer_*` series at all. Verified, not assumed.

## Decision

**Broker health enters through `prometheus-nats-exporter`, scraped by a
`prometheus` receiver, in a metrics pipeline of its own.**

- The exporter is the NATS project's own, added to the demo compose. It is a
  translator, not a dependency of either package — nothing in `src/` knows it
  exists.
- The receiver applies `metric_relabel_configs` as **default deny by metric
  name**: keep `gnatsd_(varz|connz|subsz)_*` and `jetstream_*`, drop everything
  else. The exporter publishes its own `go_*`, `process_*` and `promhttp_*`
  runtime, and those stored under a job named `nats` read as broker health —
  the D3.6 failure inverted, a dashboard reporting healthy while the thing it
  describes is not.
- `transform/nats` sets `messaging.system` and nothing else. Prometheus label
  names cannot contain dots, so the mapping cannot live in relabel config. It
  runs **before** `transform/allowlist`, so what it writes is governed by the
  same rule that governs a span attribute rather than being injected past it.
- It is a **separate pipeline** (`metrics/nats`), not a second receiver on the
  existing one, so an unconditional broker mapping can never touch a service's
  metrics. `transform/allowlist` is still in it, and the contract test now
  enumerates pipelines from the file rather than checking three by name — a new
  pipeline is precisely how an unfiltered path gets added.

**No stream or consumer label is mapped.** The exporter emits none against the
reference services, and writing `stream_name` → `messaging.destination.name`
against a specification rather than a dump is the failure
[ADR-0018](./0018-allowlist-composition.md) exists to prevent. The mapping gets
written when a dump shows the labels.

## Consequences

Dashboard 3 gains a data path, not its panels. What arrives is core-NATS server
health: connection counts, subscription counts, in/out bytes and messages,
`gnatsd_varz_slow_consumers` and `gnatsd_connz_pending_bytes`.

`gnatsd_varz_slow_consumers` is the honest core-NATS answer to "is the consumer
falling behind" — it counts connections the server disconnected for not keeping
up. It is **not** consumer lag. Lag is a number of messages waiting; a slow
consumer count is a number of subscribers already dropped, after the data is
gone. Panelling one as the other would hide exactly the twenty-minute backlog
D3.6 describes, the same substitution refused for DLQ depth against
`screening.applications.abandoned`.

So 3.2 stays ⚠️, and 3.1, 3.3 and 3.6 stay ❌. **Closing them requires moving
the reference services to JetStream**, which is a change to what the sample
demonstrates and is out of scope here —
[ADR-0022](./0022-demo-first-resequencing.md) made core NATS a deliberate demo
choice and `samples/README.md` says so. This ADR does not reverse that; it
records that the broker-side data path is no longer the blocker, and the sample's
messaging model is.

The estate cost is one more container to run and one more thing that can be
down. A dead exporter is silent by default: the scrape fails, the series stop,
and no panel says why. `e2e-instrumented.sh` starts the exporter and asserts the
series reach the sink, so the wiring is tested; monitoring the scrape itself
belongs to dashboard 4.3–4.6, which is unbuilt for the separate reason that
nothing scrapes the collector's own endpoint either.
