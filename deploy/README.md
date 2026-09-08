# Deployment — demo only

🔒 **This configuration is for staging or synthetic workloads only.** It has no
allowlist enforcement, no access tiers, and no sampling policy. Per
[ADR-0022](../docs/adr/0022-demo-first-resequencing.md) those are deferred until
after the demo, and the boundary that makes deferring them safe is that
**production KYC traffic does not pass through this stack.**

## Shape

```
service ──────OTLP/http──────▶ collector ──▶ SigNoz
               :4318          ▲              (traces, metrics, UI)
                              │
NATS ──JSON──▶ nats-exporter ─┘
     :8222                 scraped :7777
```

Two hops rather than one, deliberately. The collector is where the allowlist,
pattern scanning, and agent-service governance will attach
([ADR-0003](../docs/adr/0003-runtime-allowlist-at-source.md),
[0004](../docs/adr/0004-free-text-telemetry-and-exceptions.md),
[0009](../docs/adr/0009-governing-agent-instrumented-services.md)). Pointing
services straight at the store now would mean re-pointing every one of them
later.

## SigNoz

Chosen for the demo without a bake-off — one system to operate beats four while
proving a point. Reversible: the queries are specified store-neutrally in
[`../docs/diagnostic-queries.md`](../docs/diagnostic-queries.md), so
[the bake-off](../docs/phase3/store-bakeoff.md) still happens and loses nothing.

```sh
git clone -b main https://github.com/SigNoz/signoz.git
cd signoz/deploy/docker
docker compose up -d
```

Then run this collector alongside it, mounting `collector/config.yaml`.

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
- [ ] Allowlist processor generated and attached at the collector
- [ ] Access tiers enforced ([ADR-0020](../docs/adr/0020-telemetry-access-tiers.md))
- [ ] Sampling policy set from measured volume
- [ ] Queue sized from a measured restore window, not the placeholder here
