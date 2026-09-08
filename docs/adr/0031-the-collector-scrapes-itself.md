# 31. The collector scrapes itself, and is not exempt from its own allowlist

Date: 2026-09-08

## Status

Accepted — extends [ADR-0030](./0030-broker-health-arrives-by-scrape.md),
implements Rev 3 I3.8

## Context

`service.telemetry.metrics.level` has been `detailed` in
`deploy/collector/config.yaml` since it was written, with a comment citing
Rev 3 **I3.8** — a monitoring stack that reports healthy while dead is the
failure this exists to prevent.

The metrics were real and reached nothing. Without a configured reader they
were readable only by shelling into the container, which is not a control; it
is the same silent shape as the unregistered meter in
[ADR-0005](./0005-enforcing-the-framework-wiring.md), where an instrument
recorded correctly in-process and no data left the machine. Dashboard 4.3
through 4.6 were all marked "not exported" for this reason.

Confirmed against a running collector on 0.140.1 rather than from
documentation, because the metric set turns out to depend on what has actually
happened: with no traffic the endpoint publishes only `otelcol_process_*`, and
`otelcol_exporter_sent_spans_total` / `otelcol_exporter_send_failed_spans_total`
appear only once an export attempt has *resolved* — a collector retrying
forever against a dead endpoint publishes neither.

## Decision

**A `pull` prometheus reader on `localhost:8888`, scraped by a
`prometheus/collector` receiver, into its own `metrics/internal` pipeline.**

- `localhost`, not `0.0.0.0`. The reader and the receiver are the same process,
  so the endpoint never needs to leave it.
- `metric_relabel_configs` keep `otelcol_*` and drop everything else. The same
  endpoint also publishes the OTLP receiver's `http_server_*`, the gRPC
  exporter's `rpc_client_*` and `promhttp_*`'s own counters. Those restate the
  `otelcol_` series one layer down and disagree with them at the edges, so
  panelling either is a coin toss about which layer's definition is meant.
- **The self-report goes through `transform/allowlist` like everything else.**
  A stack exempt from the rules it enforces is the shape of every monitoring
  system that lies, and the exemption would look defensible here, which is
  exactly why it is asserted against by name.
- Its own pipeline, so `transform/nats` cannot reach it — an unconditional
  `messaging.system` would label the collector as a broker.

## Consequences

Dashboard 4.3 (spans received / dropped) and 4.5 (export failure rate) are
buildable. 4.4 and 4.6 are partly buildable and the remainder does not exist:

- **4.4 asks for queue depth *and queue age*.** `otelcol_exporter_queue_size`
  and `_queue_capacity` give depth. There is no age metric. A full queue and a
  stuck queue look identical in depth, and only the second is an incident.
- **4.6 asks for memory, ingestion *and disk*.** `otelcol_process_memory_rss_bytes`
  and the receiver counters give the first two. The `file_storage` extension
  backing the sending queue publishes nothing, so disk consumption of the
  persistent queue is unmeasured — which matters because that queue is the
  durability story and its size is a placeholder (Q13).

🔒 **This cannot detect the collector being dead, and no panel built from it
should be read as if it could.** A dead collector stops scraping itself, so the
series simply stop; absence of data is the only signal, and absence looks
identical to a quiet period. That is what 4.11's dead man's switch is for
(Rev 3 **I3.9**), it needs alerting rather than a panel, and it remains ❌.

The loop is real and converges: exporting these metrics increments the exporter
counters the next scrape reads. The series count is fixed, so each scrape adds
one batch, not one batch per batch.

One honest cost: `memory_limiter` sits in this pipeline, so under memory
pressure the collector may drop the telemetry describing its own memory
pressure. Removing it would let self-telemetry push a struggling collector over,
which is worse. The dead man's switch is the answer to that too.
