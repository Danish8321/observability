# Collector queue filling

**Means:** an exporter's persistent queue has been more than 80% full for ten
minutes. When it reaches 100%, new data for that exporter is dropped, and that
data is gone. This is the last warning before loss.

**First query** (Mimir, panel 4.4):

```promql
max by (exporter) (otelcol_exporter_queue_size / otelcol_exporter_queue_capacity)
```

**Likely causes**

- An export outage that has lasted long enough to fill the queue. "Export
  stalled" fired earlier for the same exporter; fix the store and the queue
  drains.
- No outage, but the store accepts data more slowly than it arrives. The sent
  rate is above zero and the queue still rises. Scale the store, or the
  collector's `sending_queue.num_consumers`.

The queue size (10,000 batches) is a placeholder, not sized from a measured
outage. If this fires during short outages, raise it, and size the volume at
`/var/lib/otelcol/storage` to match.

A drained queue replays at-least-once. Span-derived rates spike as it drains;
that is the backlog, not an incident.

**Owner:** Infra.
