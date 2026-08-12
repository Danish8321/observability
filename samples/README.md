# Reference service: screening

A worked KYC screening flow, instrumented the way estate services are expected
to be. It is a sample you can run and the guide for writing the next service.

```
POST /applications ──▶ screening-api ──▶ CouchDB (read, write)
                            │
                         NATS publish
                            │
                            ▼
                      screening-worker ──▶ CouchDB (read, write)
                       retry ×3 ──▶ abandon
```

| Project | What it demonstrates |
|---|---|
| `Screening.Domain` | ActivitySource and Meter declaration, repository spans, publisher, decision logic |
| `Screening.Api` | One-call setup, correlation middleware, Class 2 on the server span |
| `Screening.Worker` | Consumer spans, retry versus redelivery, abandonment as a signal |

---

## The patterns, and why each one is there

### One ActivitySource and one Meter per service, declared once

`ScreeningTelemetry` — static, process-wide, versioned.

The version is exported as `otel.scope.version`, and it is how you tell "this
span is missing" from "this service is running a build that never emitted it".
That distinction costs an hour during an incident when it is absent.

**Register the source or it emits nothing:**

```csharp
o.ActivitySources.Add(ScreeningTelemetry.ActivitySourceName);
```

Unregistered sources fail silently. This is the second most common wiring
mistake, after the collector endpoint.

### Span names describe the work, not the mechanism

`screen application`, not `ProcessMessageAsync`. A trace is read during an
incident by someone who did not write the code, and a stack of method names
tells them nothing about what the system was trying to do.

### Span kind is what draws the message hop

`ActivityKind.Producer` on publish, `ActivityKind.Consumer` on receive. Get
these wrong and the two spans render as unrelated operations instead of a hop —
the exact relationship the demo depends on.

### Class 2 identifiers go on spans, never on metrics

`KycTelemetry.SetApplicationId()` tags the current span. There is deliberately
**no metric equivalent**, and a test asserts that no such method appears.

A metric is pre-aggregated per unique dimension combination. `outcome` has two
values forever; `application.id` has one per application, forever. The first is
a metric, the second is a memory leak with a dashboard.

The division of labour: **metrics answer "how many", traces answer "which
ones".** Reach for the trace store rather than adding a dimension.

### Four identifiers, four lifetimes

| Identifier | Lifetime | Minted by |
|---|---|---|
| `trace_id` | One request | SDK |
| `session.id` | One browser page-load | Browser |
| `correlation.id` | One business workflow — may span days and many traces | Whoever starts the workflow |
| `causation.id` | The message that caused this work | The consumer |

`CorrelationMiddleware` continues a supplied correlation or mints one. It
echoes it back in a response header, because that is the identifier a human
pastes into a search box from a support ticket.

`causation.id` earns its place at 3am: it is what separates a **redelivery**
(same message, new trace) from a **retry** (same trace, new attempt). Without
it, duplicate-looking data gets dismissed as a telemetry bug and a real
redelivery storm goes unnoticed.

The correlation is also written onto the CouchDB document. Telemetry retention
is short; business record retention is not, and a workflow should stay
reconstructable after its traces have aged out.

### Retry is an event; failure is a status

```csharp
activity?.AddEvent(new ActivityEvent("screening.retry", ...));   // attempt 1, 2
activity?.SetStatus(ActivityStatusCode.Error, ex.Message);        // gave up
```

Marking a retryable attempt as an error makes the error rate meaningless, and a
meaningless error rate gets ignored — which is worse than not having one.

### Abandonment is its own counter

`screening.applications.abandoned` is separate from `screened`, because a
failure is the *absence* of an outcome rather than one of them. Merged, they
hide the exact case this platform exists to surface: **the API returned 202 and
the work silently never completed.**

### Not-found is not an error

```csharp
if (response.StatusCode == HttpStatusCode.NotFound)
{
    activity?.SetTag("application.found", false);
    return null;
}
```

Recording expected outcomes as errors trains people to ignore errors.

### Structured logs, never interpolated

```csharp
logger.LogInformation("Screened {ApplicationId}", id);     // queryable
logger.LogInformation($"Screened {id}");                   // banned (ADR-0004)
```

An interpolated string is one opaque line — unqueryable, unscannable, and
impossible to redact after the fact.

### CouchDB is HTTP, and that is the whole risk

No database instrumentation package. `HttpClient` instrumentation produces the
spans, which is why CouchDB was nearly free to instrument.

The same fact is the exposure: `GET /kyc/{docid}` puts the document identifier
in `url.full`.

```csharp
o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
```

🔒 **Exact host match, and it fails open.** Wrong value means no redaction and
no error. Verify on a real span before trusting it.

### Class 3 is stored, never emitted

`Applicant` is written to CouchDB and appears in no span tag, no log property,
and no metric dimension. It is also not returned by the read endpoint —
a diagnostic read should not become a route to personal data.

---

## Run it

```sh
docker network create observability

# SigNoz's own docker-compose install (deploy/docker in the upstream repo) is
# deprecated — install via the Foundry installer instead, then finish the
# one-time org signup at http://localhost:8080 before OTLP receivers come up:
curl -fsSL https://signoz.io/foundry.sh | bash
foundryctl cast -f casting.yaml

# SigNoz's Foundry ingester already owns host port 4318, so this repo's own
# collector is remapped to 4319 (deploy/docker-compose.yaml) — point services
# at it explicitly:
docker compose -f deploy/docker-compose.yaml up -d
curl -X PUT http://admin:password@localhost:5984/kyc

Otlp__Endpoint=http://localhost:4319 dotnet run --project samples/Screening.Api      # terminal 1
Otlp__Endpoint=http://localhost:4319 dotnet run --project samples/Screening.Worker   # terminal 2
```

### Happy path

```sh
curl -X POST http://localhost:5206/applications \
  -H 'Content-Type: application/json' \
  -H 'X-Session-Id: sess-demo-1' \
  -d '{"applicationId":"app-1001","applicant":"Dummy Person"}'

curl http://localhost:5206/applications/app-1001    # status: screened
```

One trace, both services, the NATS hop, four CouchDB calls.

### The faults

Keyed off the identifier, so they fire from a request with no redeploy.

| Send | Behaviour | What it teaches |
|---|---|---|
| `app-1002` | Normal | Baseline |
| `app-slow-1003` | 3s stall in a named span | Latency located where it happened, not where it was noticed |
| `app-fail-1004` | Fails, retries 3×, abandons | **The strong one** |

```sh
curl -X POST http://localhost:5206/applications \
  -H 'Content-Type: application/json' \
  -d '{"applicationId":"app-fail-1004","applicant":"Dummy Person"}'
# 202 Accepted

curl http://localhost:5206/applications/app-fail-1004
# status: received — never screened
```

The trace shows two retry events, then an error status, then
`screening.abandoned`. The 202 is still sitting in the caller's log.

## Verify the wiring before trusting anything

In order — each step fails differently.

1. Collector logs show received spans → export works (port **4319** in this
   demo's remapped compose, 4318 upstream default — never 4317)
2. One service in SigNoz → export works end to end
3. Two services on one trace → HTTP propagation works
4. NATS hop on the same trace → **most likely step to fail**
5. `url.full` reads `/kyc/{docid}` → redaction is on
6. Search by `correlation.id` and get the whole workflow → this is the demo

## Honest limits

- **Core NATS, not JetStream.** No redelivery: exhausting the retry loop loses
  the message. Correct for a demo, wrong for production — and JetStream changes
  the diagnostic picture, since redelivery duplicates spans
- **No allowlist enforcement.** Dummy data only (ADR-0022)
- **Sampling 1.0**, credentials in the compose file — demo settings
- **The fault injection block is demo scaffolding.** Delete it before this
  shape goes anywhere real
