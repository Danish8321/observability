# Screening slow

**Means:** the 95th percentile of `screening.duration` on one worker has been
above 2.5 s for ten minutes, with at least 20 screenings in the window. A
healthy screening takes about 10 ms: one CouchDB read, the decision, one
CouchDB write. Nobody upstream sees this: the API answered `202` long before
the worker started, so decisions arrive late without any request failing.

**First query** (Mimir): the worker's 95th percentile over the last ten
minutes. On a panel, the dots are exemplars: each opens the trace of one
screening that took that long.

```promql
histogram_quantile(0.95, sum by (deployment_environment_name, job, le) (rate(screening_duration_bucket[10m])))
```

Then the traces (Grafana Explore, Tempo, TraceQL). Every trace slower than 2 s
is stored, whatever the sampling rate (ADR-0039). Under `screen application`,
the widest child is where the time went: `sanctions list lookup` is the
screening provider, the HTTP client spans are CouchDB.

```traceql
{ resource.service.name = "screening-worker" && name = "screen application" && duration > 2s }
```

**Likely causes**

- The screening provider is slow (`sanctions list lookup`).
- CouchDB is slow to answer the worker.
- The worker is short of CPU. It screens one message at a time, so a slow
  screening also delays every message behind it.

**Owner:** the screening service's team. The platform carries the alert; the
fix is in the service or what it calls.
