# Alerting heartbeat

**Means:** this alert always fires. It arrives once a day to say that Grafana is
evaluating rules and that its email reaches you.

The case it covers is the one no other alert can: Grafana itself, or its mail
delivery, being down. Alerts evaluated inside Grafana cannot report Grafana
dying. So the signal is absence: **a day without this email is the alert.**

**What to do when it arrives:** nothing.

**What to do when it stops:** treat every other alert as unknown until it is
back.

1. Is Grafana running and serving? Open it.
2. Is the rule evaluating? *Alerting → Alert rules → Alerting heartbeat*
   should be *Firing*. If it is *Normal* or *Error*, rule evaluation has
   stopped.
3. Is mail leaving? Grafana's log records every failed send. Check the SMTP
   settings (`GF_SMTP_*`) and SES: sending limits, a suppressed recipient,
   credentials.

This is a residual: the heartbeat needs a person to notice an absence. An
external check that pages when the email stops is the upgrade, and needs a
service outside Grafana (plan M9).

**Owner:** Infra.
