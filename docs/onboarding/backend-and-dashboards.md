# The backend and the four dashboards

Continues [`integrating-a-service.md`](./integrating-a-service.md) at **A12**.
That guide ends where a service is emitting and the collector is receiving.
This one ends where an on-call engineer can answer a diagnostic question at
2am — which is the only definition of "set up" that matters, since the goal is
reduced MTTR rather than installed OpenTelemetry.

**Four dashboards, no more** — Rev 3 **I4.3**. The panels are specified
store-neutrally in [`../diagnostic-queries.md`](../diagnostic-queries.md)
([ADR-0016](../adr/0016-diagnostic-queries-are-the-durable-asset.md)); this
document is that specification built once, against the demo store.

🔒 **Everything below is the demo stack** — SigNoz, chosen without a bake-off
([`deploy/README.md`](../../deploy/README.md)), no access tiers, sampling at
1.0. Production KYC traffic does not pass through it
([ADR-0022](../adr/0022-demo-first-resequencing.md)). The last section says
what has to be true before that changes.

---

## Step D1 — the store

SigNoz ships its own compose file. Do not copy it into this repo; it drifts.

```sh
docker network create observability      # both stacks attach to this
git clone -b main https://github.com/SigNoz/signoz.git
cd signoz/deploy/docker
docker compose up -d
```

Attach SigNoz to the `observability` network, because the collector exports to
it by container name (`signoz-ingester-1:4317`, `deploy/collector/config.yaml`).
Renaming that container silently breaks export with no error at the service.

## Step D2 — the collector, alongside

```sh
cd <this repo>
docker compose -f deploy/docker-compose.yaml up -d
```

Two hops rather than one, deliberately: the collector is where the allowlist,
pattern scanning and agent-service governance attach
([ADR-0003](../adr/0003-runtime-allowlist-at-source.md),
[0004](../adr/0004-free-text-telemetry-and-exceptions.md),
[0009](../adr/0009-governing-agent-instrumented-services.md)). Services point
at the collector, never at the store.

**Port 4319, not 4318**, on this compose. SigNoz's own ingester already holds
host 4318, so this collector's host mapping moved. Inside the network it is
still 4318. `Otlp:Endpoint = http://localhost:4319` from the host.

That compose also brings up `nats-exporter`, the one thing here the collector
*pulls* from rather than receives. NATS runs none of our code and has no agent,
so its health arrives by scrape or not at all
([ADR-0030](../adr/0030-broker-health-arrives-by-scrape.md)). A dead exporter is
silent — the scrape simply fails — so if dashboard 3's broker panels are empty,
check `curl localhost:7777/metrics` before suspecting the pipeline.

## Step D3 — prove data is queryable before building anything

Three checks, in this order. Each isolates a different failure; a dashboard
built before these pass will look empty and tell you nothing about why.

1. **Spans arrive at the collector.** `docker logs` on the collector container.
   Nothing here means endpoint or firewall, not your code.
2. **Spans arrive at the store.** SigNoz Traces, filtered to your
   `service.name`. Present at the collector but absent here is an export
   failure — check the collector's own logs for `otlp/signoz`.
3. **A dropped key is visible as a dropped key.** Query the counter
   `raksawi.telemetry.attributes.dropped`, dimensioned by `attribute.key` and
   `telemetry.signal`. This is the
   [ADR-0003](../adr/0003-runtime-allowlist-at-source.md) feedback loop: without
   it, an incomplete allowlist and instrumentation-not-running are the same
   picture.

---

## Dashboard 1 — Service golden signals

| # | Panel | Built from | Status |
|---|---|---|---|
| 1.1 | Request rate | `http.server.request.duration` count, by `service.name`, `http.route` | ✅ |
| 1.2 | Error rate | same, filtered `http.response.status_code >= 500` | ✅ |
| 1.3 | Latency p50/p95/p99 | same histogram, by `http.route` | ✅ |
| 1.4 | Saturation | runtime meter — GC, allocation, thread pool | ⚠️ partial |
| 1.5 | Instances serving a service | `service.instance.id` ([ADR-0008](../adr/0008-service-instance-identity.md)) | ✅ |
| 1.6 | SQL call rate and latency | `db.operation.name` | ❌ nothing emits it |

**1.2 — do not dimension by `error.type`.** The ASP.NET Core instrumentation
emits it, and `error.*` is in no allowed family
([`../allowlist.md`](../allowlist.md)), so it is dropped before it reaches the
store. `http.response.status_code` is the allowlisted way to ask the same
question. Confirm on the dropped-key counter from D3 rather than assuming.

**1.4 is partial, and the missing half is the half people ask for.**
`AddRuntimeInstrumentation()` is wired on both targets
(`RaksawiObservabilityExtensions.net10.cs:73`, `RaksawiObservability.net48.cs:84`),
which gives GC, allocation rate and thread pool. **CPU and working set come from
process instrumentation, which is not wired at all** — no
`AddProcessInstrumentation()` anywhere in `src/`. Panel 1.4 as specified cannot
be completed today. Read the exact series names off the store rather than
copying them from here; they differ between the .NET 9+ built-in meter
(`dotnet.gc.*`) and the 4.8 package (`process.runtime.dotnet.*`).

**1.6 is correctly empty.** The estate database is CouchDB
([ADR-0023](../adr/0023-couchdb-changes-the-database-surface.md)); CouchDB
traffic appears as HTTP spans, not `db.*` spans. Build this panel when the
first SQL service onboards, not before.

## Dashboard 2 — Trace explorer

Mostly SigNoz's built-in trace search. What makes it work is that the
correlation identifiers are declared Class 2 keys and survive to the store.

| # | Panel | Entry point | Status |
|---|---|---|---|
| 2.1 | Show me this trace | `trace_id` | ✅ |
| 2.2 | Every trace in this workflow | `correlation.id` | ✅ |
| 2.3 | Everything one page load did | `session.id` | ✅ |
| 2.4 | This message and what caused it | `message.id`, `causation.id` | ✅ |
| 2.5 | Slowest traces for a route | `http.route`, duration | ✅ |
| 2.6 | Error traces, kept at 100% | tail sampling policy | ❌ no tail policy |
| 2.7 | Every span for this applicant | `application.id` — 🔒 Class 2 | ✅ |

**2.6 does not hold yet.** `deploy/collector/config.yaml` has no
`tail_sampling` processor. The demo samples at 1.0 head-based, so errors are
kept only because *everything* is kept. The panel will appear to work and will
silently stop working the moment sampling drops below 1.0 — which is what
[ADR-0010](../adr/0010-sampling-defaults.md) expects in production. Treat
2.6 as unbuilt regardless of what the demo shows.

## Dashboard 3 — Async pipeline health

🔒 **This dashboard cannot be built today.** Saying so is the point: it covers
the failure HTTP monitoring structurally cannot see — Rev 3 **D3.6**, a system
showing perfect latency, zero errors and 30% CPU while screening runs twenty
minutes behind. It is the dashboard that most needs to exist and the one
furthest from existing.

| # | Panel | Needs | Status |
|---|---|---|---|
| 3.1 | Oldest unprocessed message age | nothing emits it | ❌ |
| 3.2 | Consumer lag / backlog depth | `gnatsd_varz_slow_consumers` — a proxy | ⚠️ no lag exists to read |
| 3.3 | DLQ depth and arrival rate | no DLQ exists | ❌ |
| 3.4 | Retry count | retries are span events, not a metric | ⚠️ |
| 3.5 | Processing duration p95/p99 | `screening.duration` | ✅ |
| 3.6 | What is in the DLQ, by workflow | no DLQ exists | ❌ |

Two blockers, and neither is the one this document used to name. The broker
scrape landed on 2026-09-08 ([ADR-0030](../adr/0030-broker-health-arrives-by-scrape.md))
and the data path is no longer what is missing:

1. **The reference services use core NATS, so there is no lag to read.**
   `ScreeningConsumer.cs` subscribes with `nats.SubscribeAsync` — core pub/sub,
   no stream, no durable consumer, no backlog. A scrape of the running demo
   returns `jetstream_server_total_streams 0` and no `jetstream_consumer_*`
   series at all. 3.1, 3.2, 3.3 and 3.6 all describe a queue, and the sample
   does not have one. Closing them means moving the sample to JetStream, which
   changes what it demonstrates ([ADR-0022](../adr/0022-demo-first-resequencing.md)
   made core NATS a deliberate demo choice).
2. **The reference service has no DLQ.** `samples/` retries three times and
   abandons, incrementing `screening.applications.abandoned`. That counter is a
   genuine signal — build it as an abandonment-rate panel — but it is not DLQ
   depth, and substituting one for the other would hide 3.3's failure mode.

**The dimension is no longer a blocker, and never was one.** Every panel above
is specified as dimensioned by `messaging.consumer.group.name`. An earlier
revision of this document claimed that key was dropped in-process because
`messaging.*` was admitted key-by-key; that was wrong — `messaging.` was
allowed as a whole family prefix in both the library and the collector, so
nothing was being dropped. The enumeration the allowlist's stability policy
called for landed on 2026-09-08 ([`../allowlist.md`](../allowlist.md)): thirteen
keys, exact-match, `messaging.consumer.group.name` admitted ahead of use
precisely so this dashboard is not blocked on an allowlist review when the
instrumentation arrives. What is missing is the instrumentation, not the
permission.

**What the scrape does deliver, and what it must not be panelled as.**
`prometheus-nats-exporter` translates NATS's JSON monitoring endpoint into
prometheus text — `:8222` is not a prometheus endpoint and the collector has no
NATS receiver, so there is no configuration of the collector alone that reads
it. Through it arrive connection and subscription counts, in/out bytes and
messages, `gnatsd_connz_pending_bytes` and `gnatsd_varz_slow_consumers`.

`gnatsd_varz_slow_consumers` is the honest core-NATS answer to "is the consumer
falling behind": it counts connections the server *disconnected* for not keeping
up. It is not lag. Lag is messages waiting; a slow-consumer count is subscribers
already dropped, after the data is gone. Panel it as 3.2 and the twenty-minute
backlog D3.6 describes reads as a flat zero — the same substitution refused
above for DLQ depth against `screening.applications.abandoned`.

3.4 is answerable from span events today (`samples/README.md`, retry-as-event)
but not as a metric, so it is a trace query rather than a panel.

## Dashboard 4 — Stack health

The stack is a production dependency, and a stack that monitors itself reports
perfect health while dead — Rev 3 **I3.8**, **I3.9**.

| # | Panel | Built from | Status |
|---|---|---|---|
| 4.1 | Coverage — reporting ÷ expected | service register denominator | ❌ no register file |
| 4.2 | Freshness, ingress to queryable p95 | synthetic probe | ❌ |
| 4.3 | Spans received / dropped | collector internal metrics | ⚠️ not exported |
| 4.4 | Queue depth and queue age | same | ⚠️ not exported |
| 4.5 | Export failure rate | same | ⚠️ not exported |
| 4.6 | Collector memory, ingestion, disk | same | ⚠️ not exported |
| 4.7 | Query latency | SigNoz's own telemetry | ⚠️ |
| 4.8 | Backup status, age of last **restored** backup | nothing | ❌ |
| 4.9 | **Dropped attribute keys, by key** | `raksawi.telemetry.attributes.dropped` | ✅ |
| 4.10 | W3C format warnings, by service | `raksawi.telemetry.trace_context.corrected` | ✅ .NET 10 |
| 4.11 | Dead man's switch, four states | alerting | ❌ |

**4.3 through 4.7 are one piece of work, not five.** The collector's
`service.telemetry.metrics.level` is already `detailed`, so the data exists —
nothing scrapes or forwards it. One `prometheus` receiver pointed at the
collector's own endpoint, exported down the existing metrics pipeline, lights
all four.

**4.10 reads a gauge, not a counter.**
[ADR-0005](../adr/0005-enforcing-the-framework-wiring.md) specifies that the
startup W3C check "emit a loud warning **and increment a metric**", reasoning
that "a warning in a log on an IIS host is not a control". The metric was
missing entirely until 2026-09-08 and shipped as
`raksawi.telemetry.trace_context.corrected`, an observable gauge — 1 where the
format had to be corrected, 0 where it was already W3C. Panel it as a sum or a
max by `service.name`; every reporting process contributes a point, so a
non-zero sum is the count of affected services.

Healthy processes report 0 rather than nothing, deliberately: "the check ran
and the format was fine" and "nothing is reporting" are different answers, and
4.1 is the panel that answers the second one.

🔒 **Verified on .NET 10 only, and that is now the only runtime.**
`e2e-instrumented.sh` asserts the series reaches the sink, which is what proves
the meter is subscribed rather than recording in-process. The value-1 case is
the .NET Framework default, and 4.8 left the build entirely on 2026-09-08
([ADR-0029](../adr/0029-net48-deferred-out-of-the-build.md)) — so this panel
will read 0 across the estate until 4.8 resumes, and a 0 there means "no 4.8
service is reporting", not "no 4.8 service has a split trace".

**4.1 needs the register to exist as a file.**
[ADR-0021](../adr/0021-service-register-is-the-coverage-denominator.md) makes
the PR-gated service register the coverage denominator; no register file is in
this repo yet. Coverage against an implicit denominator is not coverage — it
cannot detect the fail-closed agent service that was never enumerated, which is
the specific failure it exists to catch
([ADR-0009](../adr/0009-governing-agent-instrumented-services.md)).

---

## What this leaves

Thirteen panels of thirty are buildable today, eight more are partial, and nine
have no data source at all. The gaps name concrete missing pieces, in rough
order of value per unit of work:

| Work | Unblocks |
|---|---|
| `prometheus` receiver on the collector's own endpoint | 4.3, 4.4, 4.5, 4.6 |
| `AddProcessInstrumentation()` | 1.4 |
| Service register file (ADR-0021) | 4.1 |
| Tail sampling policy | 2.6 |
| Move the sample to JetStream, with a DLQ | 3.1, 3.2, 3.3, 3.6 |
| Synthetic probe, alerting, backup restore check | 4.2, 4.8, 4.11 |

**Replay produces duplicates.** Every panel above derived from span counts
spikes when a backlog drains — an artifact, not an incident (Rev 3 **I3.6**).
No alert built on one may fire on a draining backlog. That is a test in the
failure matrix ([`../phase3/failure-matrix.md`](../phase3/failure-matrix.md)),
not a note here.

## 🔒 Before this carries production traffic

The demo stack is not a production stack, and the difference is not
configuration polish:

- **Access tiers** ([ADR-0020](../adr/0020-telemetry-access-tiers.md)) — two
  tiers, operational and audit, with the audit tier at two to four people. A
  store that cannot enforce the separation is disqualified from the bake-off
  ([`../phase3/store-bakeoff.md`](../phase3/store-bakeoff.md)) regardless of how
  it performs.
- **Sampling set from measured volume**, not 1.0.
- **Queue sized from a measured restore window**, not the placeholder in
  `deploy/collector/config.yaml`.
- **The bake-off actually run.** SigNoz here was chosen without one, and
  everything above is portable precisely because the questions — not their
  serialised form — are the durable asset (ADR-0016).
