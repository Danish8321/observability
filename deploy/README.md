# Deployment — demo only

🔒 **This configuration is for staging or synthetic workloads only.** It has no
allowlist enforcement, no access tiers, and no sampling policy. Per
[ADR-0022](../docs/adr/0022-demo-first-resequencing.md) those are deferred until
after the demo, and the boundary that makes deferring them safe is that
**production KYC traffic does not pass through this stack.**

## Shape

```
service ──OTLP/http─▶ collector ──▶ SigNoz
          :4318                     (traces, metrics, UI)
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
