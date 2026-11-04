# Export stalled

**Means:** the exporter named in the alert has data waiting in its persistent
queue and has sent nothing for five minutes. The collector is receiving; it
cannot deliver to the store behind that exporter. The queue holds the data
until it is full, then drops new data (see
[collector-queue-filling](./collector-queue-filling.md)).

| Exporter | Store |
|---|---|
| `otlphttp/mimir` | Mimir (metrics) |
| `otlphttp/loki` | Loki (logs) |
| `otlp/signoz` | the trace store |

The condition is the queue holding work while the sent rate is zero, not the
failed counter: with retry enabled a failing export never resolves, so
`send_failed` stays flat through an outage (finding 07, diagnostic query 4.5).

**First query** (Mimir, panels 4.4 and 4.5):

```promql
max by (exporter) (otelcol_exporter_queue_size)
```

```promql
sum by (exporter) (rate({__name__=~"otelcol_exporter_sent_(spans|metric_points|log_records)_total"}[5m]))
```

Then the collector's log, which names the failing exporter and the store's
reply on every retry.

**Likely causes**

- The store is down. "Store unreachable" fires for Mimir and Loki in two
  minutes, before this one.
- The store is up but rejects the data: authentication, a limit
  (`429`, ingestion rate), or out-of-order samples after a long outage.
- The endpoint is wrong after a change to `LOKI_OTLP_ENDPOINT`,
  `MIMIR_OTLP_ENDPOINT` or the trace exporter.

**Owner:** Infra.
