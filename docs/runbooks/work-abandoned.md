# Work abandoned

**Means:** screening-worker gave up on at least one application. The submitter
was told `202 Accepted`, the application stays `received`, and nothing retries
it: the worker subscribes with core NATS, which has no redelivery. Every
abandonment is a person waiting on a decision that is not coming.

The alert names the `reason`:

| `reason` | What happened |
|---|---|
| `ScreeningProviderException` | The screening provider failed on all three attempts. |
| `WriteConflictException` | CouchDB refused the result as a conflict on all three attempts. |
| `not_found` | The application's document did not exist when the worker read it. Not retried. |
| any other exception type | A failure the worker does not retry, such as `HttpRequestException` with CouchDB unreachable. Abandoned on the first attempt. |

**First query** (Mimir): which reasons, and how many since each worker started.

```promql
sum by (deployment_environment_name, job, reason) (screening_applications_abandoned)
```

Then the traces (Grafana Explore, Tempo, TraceQL). Each match is one abandoned
application; the span carries its `application.id`, and the
`screen application` spans under it hold each attempt's exception.

```traceql
{ resource.service.name = "screening-worker" && name = "kyc.applications.submitted process" && status = error }
```

`not_found` does not match: a missing document is not the worker's own failure,
so that span's status stays unset. Its one `screen application` child is red.

**Likely causes**

- The screening provider is down or rejecting calls. A fault at the provider
  abandons every application submitted while it lasts.
- CouchDB conflicts on every attempt: something else writes the same document
  while the worker screens it.
- `not_found`: the API writes the document before it publishes, so the
  document was deleted, or the worker reads a different CouchDB or database
  than the API writes (`CouchDb:Url`).
- CouchDB unreachable from the worker.

**Owner:** the screening service's team. The platform carries the alert; the
fix is in the service or what it calls.
