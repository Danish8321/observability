# 40. Alerting as code, delivered by email

Date: 2026-11-04

## Status

Accepted — part 1 (platform alerts, plan D3). Part 2, SLOs and burn-rate
alerting, follows in D10. Implements [ADR-0013](./0013-abort-criterion-becomes-a-reordering.md)'s
promotion of alerting to Phase 1.

## Context

Until now the stack could tell you it was broken only if someone was looking at
a dashboard. Finding 07 showed the worse case: during an export outage the
failure-rate panel reads zero, because with retry on a failing export never
resolves and nothing is counted. The persistent queue (D2) holds data through
an outage; nothing told anyone an outage was happening.

The plan fixes delivery as email (Q13), and Infra operates Grafana and SES
(R6), so what this repo ships is files Grafana provisions, verified locally.

## Decision

- **Rules, contact point and notification policy are Grafana provisioning
  files** in `deploy/grafana/provisioning/alerting/`, beside the data sources
  they reference by uid. The plan named `deploy/grafana/alerting/`; the
  provisioning tree is where Grafana reads them and what Infra already mounts.
- **Email only.** SMTP is Grafana server configuration (`GF_SMTP_*`) and never
  appears in the repo. Recipients come from `ALERT_EMAIL_TO`, one list per
  environment. Locally Mailpit catches every email, so alerts are asserted on
  rather than eyeballed (`.claude/scripts/e2e-alerting.sh`).
- **One email per alert name**, so the subject says which part is broken.
  Every rule carries a `runbook_url` to `docs/runbooks/<alert>.md`.
- **The platform alerts:**

  | Alert | Fires when | Reads |
  |---|---|---|
  | Collector not reporting | the collector's self-scrape `up` absent for 2 min, then 3 min pending | Mimir |
  | Export stalled | an exporter holds queued data and sent nothing in 5 min, for 5 min | Mimir |
  | Collector queue filling | queue over 80% of capacity for 10 min | Mimir |
  | Store unreachable: Mimir / Loki | `vector(1)` against the store fails, for 2 min | Grafana's own query |
  | Alerting heartbeat | always; repeated every 24 h | nothing |

- **The collector's metrics travel the collector's own Mimir path**, so every
  rule on them answers "is the collector reporting", not "is it alive". Those
  rules treat a failed query as OK, so a Mimir outage emails the store alert
  alone rather than also blaming the collector. The store rules invert that:
  a failed query *is* the alert, evaluated by Grafana itself, so it fires with
  the whole pipeline down.
- **"Export stalled" reads the queue, not the failure counter** — held work
  with nothing sent. That is the signal finding 07 showed the failure counter
  cannot give.

**Deviation from the plan's M9 table.** The plan lists "collector receiving:
`otelcol_receiver_accepted_spans` rate = 0" as a dead man's switch. It is not
built. A quiet estate receives nothing for minutes at a time, so the rule would
fire on idleness, and an alert that fires without action gets muted (plan,
alert noise). The collector being dead is caught by "Collector not reporting";
a service that stops sending is "service stopped reporting" in D11, which reads
the register and so knows what *should* be sending.

## Consequences

- **Grafana down is detected only by absence**: the heartbeat stops arriving,
  and a person has to notice. An external dead-man's-switch service that
  emails when the heartbeat stops would close that; choosing one is Infra's.
- **Traces are not watched.** No trace store is chosen, so there is no store
  alert or trace exporter rule beyond "Export stalled", which covers any
  exporter including the trace one.
- **A broken Mimir path looks like a dead collector** until "Store
  unreachable: Mimir" says otherwise. The runbook separates the two causes
  with one Loki query.
- **Thresholds are guesses** until real traffic exists (ADR-0027), and the
  queue size behind the 80% rule is itself a placeholder. Review after two
  weeks of production traffic; an alert that fires without action is changed
  or removed, not muted.
- **Platform labels carry no Class 2 data** — they are `component`, `exporter`
  and `heartbeat`. Service alerts (D4) are where that assertion starts to bite.
