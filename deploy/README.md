# Deployment — demo only

🔒 **This configuration is for staging or synthetic workloads only.** It has no
access tiers and no sampling policy. Per
[ADR-0022](../docs/adr/0022-demo-first-resequencing.md) those are deferred until
after the demo, and the boundary that makes deferring them safe is that
**production KYC traffic does not pass through this stack.**

## Shape

```
service ──────OTLP/http──────▶ collector ──┬─▶ Loki   (logs)    ─┐
               :4318          ▲            ├─▶ Mimir  (metrics) ─┼─▶ S3 ─ Grafana :3000
                              │            └─▶ Tempo  (traces; local disk, production's store is Infra's choice)
NATS ──JSON──▶ nats-exporter ─┘
     :8222                 scraped :7777
```

Two hops rather than one, deliberately. The collector is where the allowlist
attaches — it already does — and where pattern scanning and agent-service
governance will
([ADR-0003](../docs/adr/0003-runtime-allowlist-at-source.md),
[0004](../docs/adr/0004-free-text-telemetry-and-exceptions.md),
[0009](../docs/adr/0009-governing-agent-instrumented-services.md)). Pointing
services straight at the store now would mean re-pointing every one of them
later.

## The stores

The collector, Loki, Mimir and Grafana configuration here is what Infra deploys
([`HANDOFF.md`](HANDOFF.md), plan R6); the local stack runs the same files,
with SeaweedFS standing in for S3 and Tempo, on local disk, for the trace store
Infra has yet to choose. It also runs the two reference services.

```sh
docker network create observability   # once
docker compose -f deploy/docker-compose.yaml up -d --build --wait
.claude/scripts/e2e-stack.sh          # stores answer and Grafana sees them
.claude/scripts/e2e-alerting.sh       # each platform alert emails (~15 min)
.claude/scripts/e2e-demo.sh           # an abandoned application: its trace and its email (~10 min)
```

Grafana is on `localhost:3000` (admin/admin locally), screening-api on
`localhost:5206`; alert email lands in Mailpit on `localhost:8025`.

**The services are built from source, not from packages.** Plan D4 runs the
demo "on preview packages"; here `samples/Dockerfile` builds them from this
repository with the `ProjectReference`s they already have, so the stack runs
the working tree and needs no feed. Consuming the packages is D6a's
local-feed smoke test.

## The NATS scrape

NATS runs none of our code and has no agent, so its health reaches the pipeline
by scrape or not at all ([ADR-0030](../docs/adr/0030-broker-health-arrives-by-scrape.md)).
Its `:8222` monitoring endpoint serves **JSON**, not prometheus text, and the
collector has no NATS receiver — hence `prometheus-nats-exporter` in the compose
file, translating one into the other on `:7777`.

```sh
curl -s localhost:7777/metrics | grep gnatsd_varz_slow_consumers
```

The receiver keeps `gnatsd_(varz|connz|subsz)_*` and `jetstream_*` and drops
everything else, because the exporter also publishes its own Go runtime and
those series stored under a job named `nats` read as broker health.

🔒 **This is broker health, not consumer lag.** The reference services use core
NATS, so there is no stream and no durable consumer; `jetstream_consumer_*` does
not appear at all. See dashboard 3 in
[`../docs/onboarding/backend-and-dashboards.md`](../docs/onboarding/backend-and-dashboards.md)
before panelling any of it as backlog depth.

## The collector's own health

The collector scrapes itself on `localhost:8888` and sends the result down a
`metrics/internal` pipeline — through the same allowlist as everything else,
because a stack exempt from the rules it enforces is the shape of every
monitoring system that lies
([ADR-0031](../docs/adr/0031-the-collector-scrapes-itself.md), Rev 3 I3.8).

`otelcol_receiver_accepted_spans_total`, `otelcol_exporter_queue_size`,
`otelcol_exporter_send_failed_spans_total` and `otelcol_process_memory_rss_bytes`
are the four that matter.

🔒 **`send_failed` is not the failure signal it reads as.** It increments when an
attempt *resolves*, and with `retry_on_failure` enabled a failing export never
resolves — during a store outage the sent and failed series are both absent. Use
the queue rising while `sent` is flat, and the gap between `accepted` and `sent`.

`up` is the one that says a scrape target died: 1 reachable, 0 not, per job, and
it survives both scrape jobs' metric filters.

🔒 **None of it detects the collector being dead.** A dead collector stops
scraping itself and the series simply stop, which looks identical to a quiet
period. That needs alerting, not a panel: "Collector not reporting" in
`grafana/provisioning/alerting/rules.yaml` (ADR-0040).

## Service configuration

```csharp
builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "screening-api";     // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "kyc";
    o.OtlpEndpoint = new Uri("http://localhost:4318");
    o.SamplingRatio = 1.0;               // required outside development (ADR-0010)
    o.CouchDbHosts.Add("couch.internal");
});
```

`CouchDbHosts` matters: CouchDB is plain HTTP, so nothing distinguishes it from
any other dependency at the instrumentation layer. Without it the URL treatment
in [ADR-0023](../docs/adr/0023-couchdb-changes-the-database-surface.md) does not
apply and document identifiers reach the store intact.

## Before this touches anything real

- [x] QD2 answered — document IDs are opaque, not derived from applicant data
      (2026-08-11)
- [x] Allowlist attached at the collector — `transform/allowlist`, hand-written
      rather than generated, and held against `AllowlistRules.cs` by
      `contract.sh` (2026-09-08)
- [ ] Access tiers enforced ([ADR-0020](../docs/adr/0020-telemetry-access-tiers.md))
- [ ] Sampling policy set from measured volume
- [ ] Queue sized from a measured restore window, not the placeholder here
