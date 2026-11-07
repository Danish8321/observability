# Handoff to Infra

Infra operates EKS, S3, IAM, SES and Grafana; this repo has no access to any of
them (plan R6). What this repo hands over is configuration files, each run
unchanged by the local stack (`docker-compose.yaml`) and checked by
`.claude/scripts/e2e-stack.sh`. Everything that differs between the local stack
and production is an environment variable, listed below.

Status: **D2, partial; D3; D4 in progress.** Logs, metrics, the platform alerts
and the first service alert are ready. The collector sends traces to
`TRACES_OTLP_ENDPOINT`; the store behind it is Infra's choice (ClickHouse or
Tempo; see [the stack recommendation](../docs/plans/telemetry-stack-recommendation.html)
and its open questions), and ADR-0037 is written once that choice is made.

## Files

| File | Runs as | Notes |
|---|---|---|
| `collector/config.yaml` | OTel Collector contrib `0.140.1`, the gateway | Holds the allowlist, one of our three enforcement points (ADR-0009). **Run it unmodified**; change only environment variables. |
| `collector/k8s.yaml` | merged on top: `--config=config.yaml --config=k8s.yaml` | Adds pod metadata (namespace, pod, node) through `k8sattributes`. |
| `loki/loki.yaml` | Loki `3.5.7`, monolithic, `-config.expand-env=true` | |
| `mimir/mimir.yaml` | Mimir `2.17.0`, monolithic, `-config.expand-env=true` | Promotes `deployment.environment.name` onto every series (`promote_otel_resource_attributes`, experimental in 2.17). The platform alerts depend on it to tell staging from production. |
| `grafana/provisioning/` | mounted at `/etc/grafana/provisioning` | Data sources with fixed uids `mimir`, `loki` and `tempo` (the last holds only if the trace store is Tempo); alert rules, the email contact point and the notification policy (`alerting/`, ADR-0040) reference them. Checked by `.claude/scripts/e2e-alerting.sh`. |
| `seaweedfs/` | local stack only | Stands in for S3. Not for production. |

## Environment

| Variable | Used by | Local value | Production |
|---|---|---|---|
| `TRACES_OTLP_ENDPOINT` | collector | `http://tempo:4318` | the trace store's OTLP/http endpoint |
| `LOKI_OTLP_ENDPOINT` | collector | `http://loki:3100/otlp` | Loki's service URL + `/otlp` |
| `MIMIR_OTLP_ENDPOINT` | collector | `http://mimir:9009/otlp` | Mimir's service URL + `/otlp` |
| `DEPLOYMENT_ENVIRONMENT_NAME` | collector | `local` | `production` (one gateway per environment, R5). **Must be set**: unset, it defaults to `staging`, and the platform alerts ignore staging, so a production gateway left unset is unwatched |
| `LOKI_S3_BUCKET` / `MIMIR_S3_BUCKET` | Loki / Mimir | `loki` / `mimir` | one bucket per store |
| `S3_ENDPOINT` | both | `seaweedfs-s3:8333` | `s3.<region>.amazonaws.com` |
| `S3_REGION` | both | `us-east-1` | the bucket region |
| `S3_INSECURE` | both | `true` | `false` (default) |
| `S3_FORCE_PATH_STYLE` | Loki | `true` | `false` (default) |
| `S3_BUCKET_LOOKUP_TYPE` | Mimir | `path` | `auto` (default) |
| `LOKI_RETENTION` / `MIMIR_RETENTION` | Loki / Mimir | `720h` (default) | your call (open question 6) |
| `MIMIR_OUT_OF_ORDER_WINDOW` | Mimir | `1h` (default) | at least the longest store outage the collector queue should cover |
| `MIMIR_URL` / `LOKI_URL` / `TEMPO_URL` | Grafana | `http://mimir:9009/prometheus` / `http://loki:3100` / `http://tempo:3200` | service URLs (`TEMPO_URL` only if the trace store is Tempo) |
| `GF_SMTP_ENABLED` / `GF_SMTP_HOST` / `GF_SMTP_FROM_ADDRESS` | Grafana | `true` / `mailpit:1025` / `alerts@raksawi.local` | SES SMTP endpoint, port 587, and a verified sender; add `GF_SMTP_USER` / `GF_SMTP_PASSWORD` from a secret |
| `ALERT_EMAIL_TO` | Grafana (contact point) | `oncall@raksawi.local` | the on-call list, one per environment |

No S3 access keys appear in any file. Locally they come from
`AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`; in production, leave those unset
and give Loki and Mimir an IAM role for service accounts (IRSA) with
read/write/list on their own bucket only.

## What Infra provides

- **Buckets.** Neither store creates its bucket.
- **Collector service account** allowed to `get`, `watch` and `list` pods,
  namespaces and nodes (for `k8sattributes`), and a persistent volume at
  `/var/lib/otelcol/storage` writable by uid 10001: the export queue survives
  restarts there.
- **Each service pod** sets `OTEL_SERVICE_INSTANCE_ID` from its pod name (R3).

## Recommendations, Infra decides

- **R2**: one gateway replica for now. It is a single point of failure, covered
  by the persistent queue now and by the dead-man's-switch alerts in D3.
- **R5**: staging telemetry goes to the production stack tagged
  `deployment.environment.name=staging`. The platform alerts ignore it.

## Not in this handoff yet

- **The trace store.** Locally it is Tempo on local disk (`tempo/tempo.yaml`),
  a stand-in like SeaweedFS and not for production. Until a store answers at
  `TRACES_OTLP_ENDPOINT`, traces queue and are dropped when the queue is full:
  expect "Export stalled" and later "Collector queue filling" for
  `otlphttp/traces` as soon as a service sends traces. They are true (traces
  are being dropped) and stay until the store is in place; every other
  exporter still alerts on its own.
- **Queue size** is a placeholder (10,000 batches), not sized from a measured
  outage window.
- **Exemplars** are off in Mimir by default; D4 turns them on.
- The rest of the service alerts (D4), dashboards and SLOs (D10). The
  platform alerts (D3) and "Work abandoned" are in this handoff.
- **A watcher for the heartbeat.** "Alerting heartbeat" emails daily; Grafana
  being down shows only as that email not arriving. An external
  dead-man's-switch service that emails on a missed heartbeat closes the gap;
  choosing one is yours (ADR-0040).

## Production checks that are Infra's

Restore from S3, alert email through real SES, and the MTTR baseline (R6).
