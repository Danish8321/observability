# Diagnostic query specifications

Store-neutral. Per
[ADR-0016](./adr/0016-diagnostic-queries-are-the-durable-asset.md) the durable
asset is the diagnostic question, not its serialised form. Dashboards are built
once, in whichever store is chosen, and rebuilt from here if that changes.

These also serve Rev 3 **D4.5**, which requires runbook entries naming the exact
diagnostic query per failure mode. A panel and a runbook query are the same
artifact seen from two directions.

**Four dashboards, no more** — Rev 3 **I4.3**. The constraint is deliberate: a
dashboard nobody can read during an incident is decoration.

---

## 1. Service golden signals

| # | Question | Dimensioned by | Notes |
|---|---|---|---|
| 1.1 | Request rate | `service.name`, `http.route` | Route template, never raw path |
| 1.2 | Error rate | `service.name`, `http.route`, `http.response.status_code` | |
| 1.3 | Latency p50 / p95 / p99 | `service.name`, `http.route` | Compared against the SLO, not an absolute |
| 1.4 | Saturation — CPU, working set, allocation rate | `service.name`, `service.instance.id` | |
| 1.5 | Which instances are serving this service | `service.instance.id` | Answers "which box is this?" — see [ADR-0008](./adr/0008-service-instance-identity.md) |
| 1.6 | SQL call rate and latency | `service.name`, `db.operation.name` | Never statement text |

## 2. Trace explorer

| # | Question | Entry point |
|---|---|---|
| 2.1 | Show me this trace | `trace_id` |
| 2.2 | Show me every trace in this business workflow | `correlation.id` — survives across traces and sampling |
| 2.3 | Show me everything one page load did | `session.id` — the AJAX fragmentation case |
| 2.4 | Show me this message and what caused it | `message.id`, `causation.id` |
| 2.5 | Slowest traces for this route in this window | `http.route`, duration |
| 2.6 | Traces containing an error, this service, this window | kept at 100% by tail policy |
| 2.7 | Show me every span for this applicant | `application.id` — 🔒 Class 2, traces and logs only |

Queries 2.2 through 2.4 are the reason the correlation model in
[ADR-0007](./adr/0007-correlation-lifetime.md) has five identifiers. Each row is
a question the four-identifier model could not answer cleanly.

## 3. Async pipeline health

The failure mode HTTP monitoring structurally cannot see — Rev 3 **D3.6**. A
system can show perfect HTTP latency, zero errors, and 30% CPU while the
screening pipeline is twenty minutes behind.

| # | Question | Dimensioned by | Notes |
|---|---|---|---|
| 3.1 | **Oldest unprocessed message age** | `messaging.consumer.group.name` | The single most important async signal |
| 3.2 | Consumer lag / backlog depth | `messaging.consumer.group.name` | |
| 3.3 | DLQ depth and arrival rate | `messaging.consumer.group.name` | Silent failure surfaces here |
| 3.4 | Retry count | `messaging.consumer.group.name` | Instability |
| 3.5 | Processing duration p95 / p99 | `messaging.consumer.group.name` | |
| 3.6 | What is in the DLQ, and which workflow did it belong to | `correlation.id` per message | Only answerable because DLQ preserves correlation — ADR-0007 |

🔒 Never dimensioned by message or application ID. Rev 3 **D2.1** rule 1.

## 4. Stack health

The stack is now a production dependency. A stack that monitors itself reports
perfect health while dead — Rev 3 **I3.8**, **I3.9**.

| # | Question | Notes |
|---|---|---|
| 4.1 | **Coverage** — services reporting ÷ services expected, where *expected* is the PR-gated service register ([ADR-0021](./adr/0021-service-register-is-the-coverage-denominator.md)) | Rev 3 **I4.6** SLI, target 100%. Detects a fail-closed agent service that was never enumerated ([ADR-0009](./adr/0009-governing-agent-instrumented-services.md)) and an idle-timed-out application pool |
| 4.2 | **Freshness** — ingress to queryable, p95 | Rev 3 **I4.6** SLI, target < 60s. "30 seconds behind" and "20 minutes behind" are different incidents |
| 4.3 | Spans received per second, spans dropped | |
| 4.4 | Collector queue depth and queue age | Rising queue age is the early warning |
| 4.5 | Export failure rate | |
| 4.6 | Collector memory, ingestion rate, disk usage | |
| 4.7 | Query latency | |
| 4.8 | Backup status, and age of last **restored** backup | Rev 3 **I3.7** — untested backup is not backup |
| 4.9 | **Dropped attribute keys**, by key | The [ADR-0003](./adr/0003-runtime-allowlist-at-source.md) feedback loop. Without it, an incomplete allowlist is indistinguishable from instrumentation not running |
| 4.10 | W3C format warnings, by service | The [ADR-0005](./adr/0005-enforcing-the-framework-wiring.md) startup check. A warning in an IIS log is not a control; this is |
| 4.11 | Dead man's switch — four distinct states | Applications stopped emitting · collector stopped receiving · collector stopped exporting · storage unavailable. Rev 3 **I3.8** |

## Cross-cutting requirement

**Replay produces duplicates.** Delivery is at-least-once, so a drained backlog
spikes span-derived metrics as an artifact, not an incident — Rev 3 **I3.6**.

Every panel above that derives from span counts must be verified against this
during the failure matrix, and every alert built on one must not fire when a
backlog drains. This is a test, not a note.
