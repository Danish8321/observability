# Latency

**Means:** the 95th percentile of one route's request duration has been above
2 s for ten minutes, with at least 20 requests in the window. One caller in
twenty waits longer than that. The alert names the service (`job`) and the
route. A healthy `POST /applications` is two CouchDB calls and one NATS
publish, well under 100 ms.

**First query** (Mimir): the route's 95th percentile over the last ten
minutes. On a panel, the dots are exemplars: each opens the trace of one
request that took that long.

```promql
histogram_quantile(0.95, sum by (deployment_environment_name, job, http_route, le) (rate(http_server_request_duration_bucket[10m])))
```

Then the traces (Grafana Explore, Tempo, TraceQL). Every trace slower than 2 s
is stored, whatever the sampling rate (ADR-0039); the widest child span is
where the time went.

```traceql
{ resource.service.name = "screening-api" && span.http.route != nil && duration > 2s }
```

**Likely causes**

- A dependency is slow or unreachable. A call to a host that does not answer
  is slow before it fails, so this alert often arrives with "Error rate".
- The service is short of CPU or threads: every route slows at once.
- A deployment: compare the alert's start with the last release.

**Owner:** the team that owns the service. The platform carries the alert; the
fix is in the service or what it calls.
