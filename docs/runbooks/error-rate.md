# Error rate

**Means:** more than 5% of the requests to one route of one service ended in a
5xx, for ten minutes, with at least 20 requests in the window. Callers are
being told the service failed. The alert names the service (`job`) and the
route.

**First query** (Mimir): which status codes the route returned, and how many,
over the last ten minutes.

```promql
sum by (deployment_environment_name, job, http_route, http_response_status_code) (increase(http_server_request_duration_count[10m]))
```

Then the traces (Grafana Explore, Tempo, TraceQL). Every trace with an error
is stored, whatever the sampling rate (ADR-0039); the red span furthest down is
the call that failed.

```traceql
{ resource.service.name = "screening-api" && span.http.response.status_code >= 500 }
```

From a trace, **Logs for this span** opens the log lines written inside it,
exception included.

**Likely causes**

- Something the route calls is down. For `POST /applications` that is CouchDB
  (read, then write) or NATS (publish); the failing client span says which.
- A deployment: compare the alert's start with the last release.
- The service cannot reach a dependency by name or credentials
  (`CouchDb:Url`, `Nats:Url`).

**Owner:** the team that owns the service. The platform carries the alert; the
fix is in the service or what it calls.
