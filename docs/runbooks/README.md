# Runbooks

One page per alert, linked from the alert's `runbook_url`. Each says what the
alert means, the first query to run, the likely causes and who owns it (plan M9,
Rev 3 **D4.5**). The rules are in
[`deploy/grafana/provisioning/alerting/rules.yaml`](../../deploy/grafana/provisioning/alerting/rules.yaml);
why they are shaped this way is [ADR-0040](../adr/0040-alerting-as-code-by-email.md).

| Alert | Runbook |
|---|---|
| Collector not reporting | [collector-not-reporting](./collector-not-reporting.md) |
| Export stalled | [collector-export-stalled](./collector-export-stalled.md) |
| Collector queue filling | [collector-queue-filling](./collector-queue-filling.md) |
| Store unreachable: Mimir / Loki | [store-unreachable](./store-unreachable.md) |
| Alerting heartbeat | [alerting-heartbeat](./alerting-heartbeat.md) |

**Owner** for every platform alert: Infra, who operate the collector, the
stores and Grafana (plan R6). A change to the configuration itself is a pull
request to this repository.
