# Broker metrics missing

**Means:** for five minutes the collector has failed to scrape
`prometheus-nats-exporter` (ADR-0030). Every broker series has stopped, so
"Slow consumer" cannot fire: an empty query is treated as OK. The broker itself
may be fine; this alert says only that nobody can see it.

**First query** (Mimir): the scrape's own health, 0 when it fails.

```promql
min by (deployment_environment_name, instance) (up{job="nats"})
```

Then the exporter: is it running, and can it reach the broker's monitoring
port (`:8222`)? Its log says which.

**Likely causes**

- The exporter is stopped or crash-looping.
- The exporter cannot reach the NATS monitoring endpoint, for example because
  the broker restarted without `-m 8222`.
- The scrape target in the collector configuration (`NATS_EXPORTER_TARGET`)
  points at the wrong host.

**Owner:** Infra, who run the exporter and the broker.
