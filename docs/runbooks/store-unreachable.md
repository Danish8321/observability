# Store unreachable

**Means:** Grafana has failed to query the named store (Mimir or Loki) for two
minutes. Grafana evaluates this itself, so it fires even when the collector and
the whole pipeline are down.

- **Mimir:** metrics cannot be read, and every other alert is blind: they all
  query Mimir. Their silence during this outage means nothing.
- **Loki:** logs cannot be read.

The collector holds new data for the store in its persistent queue while it is
down; nothing is lost until that queue is full.

**First query:** the data source health check, from Grafana: *Connections →
Data sources → Mimir (or Loki) → Test*. Then the store's own readiness:

```sh
curl http://<mimir>:9009/ready
curl http://<loki>:3100/ready
```

**Likely causes**

- The store process is down or restarting.
- The store is up but cannot reach its S3 bucket: credentials, the IAM role,
  or the bucket itself. The store's log names the S3 error.
- Grafana cannot reach the store: `MIMIR_URL`/`LOKI_URL` wrong, or a network
  policy.

**Owner:** Infra.
