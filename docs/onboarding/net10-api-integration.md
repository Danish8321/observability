# Full guide: integrating Raksawi.Observability into a .NET 10 API

Stepwise walkthrough for wiring a .NET 10 minimal-API or controller-based
service to `Raksawi.Observability` (and `Raksawi.Observability.Kyc` if the
service is KYC domain). For the terse reference version, see
[`integration.md`](./integration.md). Every step below is grounded in the
working reference service — `samples/Screening.Api/Program.cs` — read
alongside this, not instead of it.

## Prerequisites

- .NET 10 SDK.
- The package comes from Azure Artifacts, not this repo — this repo has no
  service code to copy, only the pattern.
- A collector reachable over OTLP http/protobuf on port **4318** (4317/gRPC
  is closed estate-wide, unsupported on the 4.8 target — irrelevant here but
  keeps the estate's exporters uniform).
- Know your service's `service.name` and `service.namespace` before you
  start — bare, kebab-case, never a hostname ([ADR-0006](../adr/0006-service-identity-convention.md)).

## Step 1 — add the package reference(s)

```sh
dotnet add package Raksawi.Observability
# only if this service is KYC domain:
dotnet add package Raksawi.Observability.Kyc
```

Both multi-target `net48;net10.0`; your `net10.0` project pulls the
`net10.0` build automatically.

## Step 2 — call `AddRaksawiObservability` before anything else touches the builder

This is the *only* telemetry setup call this service needs — traces,
metrics, logs, resource attributes, redaction, sampler defaults, and
exporter safety all follow from it.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "my-service";          // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "my-domain";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 0.1;                 // required outside Development (ADR-0010)

    // Without this, THIS SERVICE'S OWN SPANS ARE DROPPED SILENTLY. #1 wiring mistake.
    o.ActivitySources.Add(MyServiceTelemetry.ActivitySourceName);

    // Only if this service talks to CouchDB — see step 6.
    // o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
});
```

`o.OtlpEndpoint` defaults to `http://localhost:4318` if omitted — set it
explicitly per environment via config rather than relying on the default
past local dev. Setting `Endpoint` programmatically used to silently drop
the SDK's auto-appended `/v1/traces`/`/v1/metrics` path (404s); confirmed
fixed — if you see 404s from the collector, check you're on a version past
that fix before assuming it's back.

**Boot fails fast, on purpose**, if `ServiceName`/`ServiceNamespace` are
missing, or `SamplingRatio` is absent outside `Development`
([ADR-0010](../adr/0010-sampling-defaults.md)). That's the library working
as intended — these are config errors, not telemetry outages (Rev 3 I3.6:
telemetry setup never fails a service *for telemetry reasons*, but a missing
sampler is a real misconfiguration).

## Step 3 — set environment variables

```
OTEL_SERVICE_NAME=my-service
DEPLOYMENT_ENVIRONMENT=Production   # or whatever ASPNETCORE_ENVIRONMENT maps to
```

Optional: `OTEL_SERVICE_INSTANCE_ID` — stable across app-pool/process
recycles if you set it; derived from machine name if you don't
([ADR-0008](../adr/0008-service-instance-identity.md)).

## Step 4 — register your own `ActivitySource`

If your service creates its own spans beyond what auto-instrumentation
gives you (HTTP server/client, EF, etc.), define one `ActivitySource` and
register its name in step 2's `ActivitySources.Add(...)`. Forgetting this
step is the single most common failure — it emits *nothing*, no error, no
warning. See `samples/Screening.Domain/ScreeningTelemetry.cs` for the
pattern.

## Step 5 — mint or continue correlation

Four identifiers, four lifetimes (`CONTEXT.md#correlation`):

| Identifier | Lifetime | You do |
|---|---|---|
| `trace_id` | one request | nothing — SDK |
| `session.id` | one browser page-load | continue from `X-Correlation-Id` if the caller sent one |
| `correlation.id` | one business workflow | mint at workflow start if none supplied; echo it back in a response header |
| `causation.id` | direct parent message | set on consume, from the message that caused this work |

Add correlation middleware early in the pipeline, before your endpoints:

```csharp
app.UseExceptionHandler();
app.UseCorrelation();   // samples/Screening.Api/CorrelationMiddleware.cs
```

Read a minted or continued ID inside a handler via `context.CorrelationId()`
(see `CorrelationMiddleware.cs` for the extension method and header name).

## Step 6 — CouchDB (or any HTTP dependency exposing IDs in the URL)

Only if applicable. Two separate things, both required:

1. Register the host so redaction applies:
   ```csharp
   o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
   ```
2. `HttpClient` does **not** honor userinfo embedded in a URI (RFC 3986
   §3.2.1 is not followed) — credentials must go on an explicit
   `Authorization` header, or every call 401s:
   ```csharp
   builder.Services.AddHttpClient<MyRepository>(client =>
   {
       var uri = new Uri(couchDbUrl);
       client.BaseAddress = new Uri(uri.GetLeftPart(UriPartial.Authority));
       if (!string.IsNullOrEmpty(uri.UserInfo))
       {
           client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
               "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(uri.UserInfo)));
       }
   });
   ```

`CouchDbHosts` redaction is **exact host match and fails open** — a wrong or
missing host means silent non-redaction, not an error. Verify on a real span
before trusting it (step 9).

## Step 7 — NATS, if this service publishes or consumes

Register the connection with the JSON serializer explicitly — the default
serializer only handles primitives/strings, and publishing a record without
this throws `NatsException` at every call:

```csharp
builder.Services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts
{
    Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222",
    SerializerRegistry = NatsJsonSerializerRegistry.Default,
}));
```

Use `ActivityKind.Producer`/`Consumer` explicitly on both sides of a message
hop, or the two spans render as unrelated operations rather than one hop.

**If you manually start a consumer-side span** (not relying purely on
NATS.Net's own auto-instrumentation), extract the parent context from
message headers yourself — do not assume `Activity.Current` carries it
across an `await foreach` boundary. It doesn't; NATS.Net's own receive span
only lives for the duration of producing one item off the async-enumerable.

```csharp
var parentContext = NatsMsgTelemetryExtensions.GetActivityContext(message.Headers);
using var activity = MyTelemetry.Source.StartActivity(
    "process message", ActivityKind.Consumer, parentContext);
```

See `samples/Screening.Worker/ScreeningConsumer.cs` — this exact bug shipped
once (worker span was a disconnected new root instead of a child), fixed by
passing the full `NatsMsg<T>` into the handler instead of just `.Data`.

## Step 8 — KYC policy layer, if applicable

```csharp
activity?.SetApplicationId(applicationId);   // Class 2 — spans only, no metric equivalent by design
```

Data classes 3 (restricted PII) and 4 (secrets) appear **nowhere** in
telemetry — not spans, logs, or metrics. If you're about to tag something
that might be class 3/4, stop and check
[`docs/allowlist.md`](../allowlist.md) first.

### When the build says RKS001

```
error RKS001: Attribute key 'screening.outcome' is not allowlisted and will be
dropped before export.
```

This is not a lint complaint. It is the build telling you the tag you just
wrote reaches no store — the runtime allowlist drops it, and without the
diagnostic you would find out during an incident, from a query that returns
nothing.

Two ways to clear it, and only one of them is usually right:

1. **Use a key inside an allowed family.** If the thing you are tagging is
   already covered by semantic conventions (`http.`, `db.`, `messaging.`,
   `server.`, `exception.` …), use the conventional key. Free, no release.
2. **Declare it in the policy pack.** Domain vocabulary — outcomes, statuses,
   the infrastructure a call addressed — is declared individually, never as a
   family ([ADR-0025](../adr/0025-domain-attributes-are-declared-not-a-family.md)):

   ```csharp
   // src/Raksawi.Observability.Kyc/AssemblyInfo.cs
   [assembly: AllowedAttributeKey("screening.outcome", DataClass.Infrastructure)]
   ```

   Pick the data class honestly. Class 2 is an opaque business identifier and
   may never be a metric dimension (RKS002, an error, not a warning). Classes 3
   and 4 appear nowhere in telemetry, and declaring one is ignored rather than
   honoured — it is not a route to emitting it.

What you should *not* do is suppress the diagnostic. It is not protecting a
style rule; it is telling you the data will not arrive.

Note this is a **package release**, not a file edit, and that is deliberate
(ADR-0017): a vocabulary change goes through the same review as any other
schema change. Also note the analyzer only sees literal keys — a key built at
run time compiles clean and is still dropped at run time.

Two more diagnostics exist: **RKS002** (Class 2 as a metric dimension — an
error, because an unbounded dimension degrades the store for every service) and
**RKS003** (exporter configured by hand, which bypasses the allowlist entirely).

Structured logs only — `LogInformation("Screened {ApplicationId}", id)`,
never string interpolation (`ADR-0004`, banned at build, scanned at
collector).

Span names describe the work ("screen application"), not the method name.
Retry is an event (`AddEvent`), not an error status — reserve
`SetStatus(Error)` for actually giving up. Expected outcomes (not-found) are
tagged, not failed.

## Step 9 — verify before trusting any of it

1. Collector logs show received spans (port **4318**, not 4317; **4319**
   against this repo's own demo compose stack — see
   [`infra.md`](./infra.md)).
2. Your service alone reaches the backend end to end.
3. Two services show up on one trace — HTTP propagation across the hop.
4. A message hop (NATS etc.) lands on the **same** trace — most likely step
   to fail (step 7's bug is the concrete failure mode to check for).
5. Any redacted field reads correctly — `url.full` shows the redacted path,
   and no userinfo/credentials leak through either.
6. Search by `correlation.id` and get the whole workflow back — the actual
   deliverable, not span count.

## Getting stuck

- Unregistered `ActivitySource` → emits nothing, no error. Check step 4
  first.
- Wrong collector port → 4318 not 4317 (4319 on this repo's demo compose).
- Boot fails citing `SamplingRatio` → that's ADR-0010 working as intended;
  set it, don't route around it.
- NATS `NatsException` on publish → missing `SerializerRegistry` (step 7).
- CouchDB 401s → `HttpClient` dropped the userinfo; use the explicit header
  (step 6).
- Trace splits across a NATS hop → manual consumer span not extracting
  parent context (step 7).
- Anything policy/allowlist-related → `docs/allowlist.md`, then ADR-0002 /
  0003 / 0017 / 0018.

Full worked, runnable example: `samples/Screening.Api`, `.Domain`,
`.Worker`, and `samples/README.md` for the end-to-end run instructions
(SigNoz via the Foundry installer, demo compose collector on port 4319).
