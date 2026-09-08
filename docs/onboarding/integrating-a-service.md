# Integrating a service: zero to complete

You have a service. It has no telemetry. This walks you from that to a
service whose traces, metrics and logs arrive, survive the allowlist, and
answer a question at 3am.

For working *on* this repo rather than consuming it, read
[`developer.md`](./developer.md). For running the collector and backend,
[`infra.md`](./infra.md).

Everything below is grounded in a service that actually runs —
`samples/Screening.Api`, `.Domain`, `.Worker`. Read those alongside this,
not instead of it.

## What "complete" means

Not "the package is referenced". You are done when all six hold:

- [ ] The collector shows spans arriving from your service.
- [ ] Your service alone is visible end to end in the backend.
- [ ] Two services appear on **one** trace — HTTP propagation works.
- [ ] A message hop (NATS) lands on that **same** trace. Most likely step to fail.
- [ ] Redacted fields read correctly on a real span, not in theory.
- [ ] Searching `correlation.id` returns the whole workflow.

The last one is the actual deliverable. Span count is not.

Those six are per-service and end at "the data is queryable". The estate-level
half — standing the store up and building the four dashboards that make the
data *readable during an incident* — is
[`backend-and-dashboards.md`](./backend-and-dashboards.md), which picks up
where A12 leaves off.

---

## Step 0 — pick your path

| You are | Path | What you write |
|---|---|---|
| .NET 10, `IHostApplicationBuilder` | **A** | One call. [Part A](#part-a--net-10) |
| .NET Framework 4.8, can change code | **B** | One call plus three touchpoints the library cannot reach from inside. [Part B](#part-b--net-framework-48-sdk-path) |
| .NET Framework 4.8, cannot change code | **C** | Nothing. Infra installs an agent. [Part C](#part-c--net-framework-48-agent-path) |

Ask your platform contact if unsure. Paths B and C are **not
interchangeable** — B gives you two of the three enforcement points, C gives
you one.

Before you start, know two values: your `service.name` and
`service.namespace`. Bare, kebab-case, never a hostname, container name, or
app pool name ([ADR-0006](../adr/0006-service-identity-convention.md)).
`screening-api` / `kyc`, not `KYC.Screening.Api.PROD01`.

---

## Part A — .NET 10

### A1. Add the package

```sh
dotnet add package Raksawi.Observability
# only if this service handles KYC data:
dotnet add package Raksawi.Observability.Kyc
```

Both come from Azure Artifacts, not this repo. This repo has no service code
to copy — only the pattern.

The analyzer ships inside the package. From this point your build will start
telling you about attribute keys that will not survive export. That is the
point; see [A10](#a10--when-the-build-talks-back).

### A2. The one call

This is the *only* telemetry setup this service needs. Traces, metrics,
logs, resource attributes, redaction, sampling and exporter safety all
follow from it.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "my-service";        // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "my-domain";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 0.1;               // required outside Development (ADR-0010)

    // Your own spans. Without this they are dropped silently.
    o.ActivitySources.Add(MyTelemetry.ActivitySourceName);

    // Your own metrics. Same mistake, one signal over, equally silent.
    o.Meters.Add(MyTelemetry.MeterName);

    // Only if you talk to CouchDB — see A6.
    // o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
});
```

Every line is load-bearing, and **each omission fails silently except one**:

| Omitted | What happens |
|---|---|
| `ActivitySources.Add` | Your spans emit nothing. No error, no warning. |
| `Meters.Add` | Your metrics are collected by nothing. |
| `CouchDbHosts.Add` | Document identifiers reach the store in `url.full`. |
| `OtlpEndpoint` | Defaults to `http://localhost:4318`. Fine locally, wrong everywhere else. |
| `SamplingRatio` | **Boot fails** outside Development. |

That last row is deliberate asymmetry. A silently wrong sampling rate is not
detectable from the data afterwards, so ADR-0010 makes it loud. A boot
failure citing `SamplingRatio` is the library working — set it, do not route
around it. Missing `ServiceName`/`ServiceNamespace` fail the same way.

Nothing here fails a service start for a telemetry *outage* (Rev 3 I3.6). A
collector that is down does not take your service with it. These are
configuration errors, which is a different thing.

**Port 4318, http/protobuf.** Not 4317, not gRPC — 4317 is closed
estate-wide and gRPC is unsupported on the 4.8 target, so the estate's
exporters stay uniform. Against this repo's own demo compose stack the host
mapping is remapped to **4319** (SigNoz's ingester holds 4318 there); see
[`infra.md`](./infra.md).

### A3. Environment variables

```
OTEL_SERVICE_NAME=my-service
DEPLOYMENT_ENVIRONMENT=Production
```

Optional but worth setting: `OTEL_SERVICE_INSTANCE_ID`, stable across app
pool and process recycles. Derived from the machine name if you leave it
([ADR-0008](../adr/0008-service-instance-identity.md)).

If you set `OTEL_RESOURCE_ATTRIBUTES`, note that resource attributes are
allowlisted too, on a **narrower** family set than spans get
([ADR-0026](../adr/0026-resource-attributes-are-allowlisted-narrowly.md)):
identity, provenance, and where it ran. A key outside those families is
dropped on every signal, silently.

### A4. Declare your own `ActivitySource` and `Meter`

One of each per service, process-wide, created once. Creating them
per-request is the most common way to leak.

```csharp
public static class MyTelemetry
{
    public const string ActivitySourceName = "Raksawi.MyService";
    public const string MeterName = "Raksawi.MyService";

    public static readonly ActivitySource Source = new(ActivitySourceName, "1.0.0");
    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> Screened =
        Meter.CreateCounter<long>("myservice.things.done", unit: "{thing}");
}
```

Set the version. It exports as `otel.scope.version` and is how you tell "this
span is missing" from "this instance is running an old build that never
emitted it".

Then register both names in A2. Pattern:
`samples/Screening.Domain/ScreeningTelemetry.cs`.

### A5. Correlation — mint it or continue it

Five identifiers, five different lifetimes. Confusing them is why an
incident query returns one request instead of one workflow.

| Identifier | Lifetime | You do |
|---|---|---|
| `trace_id` | one request | nothing — the SDK mints it |
| `session.id` | one browser page-load | continue from `X-Correlation-Id` if the caller sent one |
| `correlation.id` | one **business workflow** — survives across traces | mint at workflow start if none supplied; echo it back in a response header |
| `causation.id` | the direct parent message | set on consume — this is what tells a redelivery from a retry |
| `message.id` | the message itself, unchanged by redelivery | set on publish |

Add the middleware early, before your endpoints:

```csharp
app.UseExceptionHandler();
app.UseCorrelation();   // samples/Screening.Api/CorrelationMiddleware.cs
```

Read the minted or continued value in a handler with
`context.CorrelationId()`.

### A6. CouchDB, or any HTTP dependency with identifiers in the URL

Skip if not applicable. Two separate things, both required.

**One — register the host so redaction applies:**

```csharp
o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
```

Exact host match, and it **fails open**: a wrong or missing host means
silent non-redaction, not an error
([ADR-0023](../adr/0023-couchdb-changes-the-database-surface.md)). Verify on a real span
before trusting it (A12, step 5).

**Two — credentials need an explicit header.** `HttpClient` ignores userinfo
embedded in a URI (RFC 3986 3.2.1 is not honoured), so every call 401s
without this:

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

### A7. NATS, if you publish or consume

Register the connection with the JSON serializer explicitly. The default
serializer handles primitives and strings only, and publishing a record
without this throws `NatsException` on every call:

```csharp
builder.Services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts
{
    Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222",
    SerializerRegistry = NatsJsonSerializerRegistry.Default,
}));
```

NATS.Net emits its own publish/subscribe spans and propagates trace context
through message headers from 3.0.1. You do not inject `traceparent` by hand,
and its source is registered for you — do not add it to `ActivitySources`.

**If you start a consumer-side span yourself**, extract the parent context
from the message headers. Do not assume `Activity.Current` carries across an
`await foreach` boundary — it does not:

```csharp
var parentContext = NatsMsgTelemetryExtensions.GetActivityContext(message.Headers);
using var activity = MyTelemetry.Source.StartActivity(
    "process message", ActivityKind.Consumer, parentContext);
```

This exact bug shipped here once: the worker span was a disconnected new
root instead of a child of the producer's trace. Fixed by passing the whole
`NatsMsg<T>` into the handler instead of just `.Data`. See
`samples/Screening.Worker/ScreeningConsumer.cs`.

### A8. Write telemetry the estate can read

These are conventions, not preferences. Each has a failure mode behind it.

**Span names describe the work, not the method.** `"screen application"`,
not `"ScreenAsync"`. Traces are read during incidents by people who did not
write the code.

**`ActivityKind.Producer`/`Consumer` on both sides of a message hop**, or the
two spans render as unrelated operations rather than one hop.

**Retry is an event, not an error status.** `activity?.AddEvent(...)` while
retrying; `SetStatus(Error)` only when actually giving up. Otherwise your
error rate counts attempts, becomes meaningless, and gets ignored.

**Abandonment gets its own counter**, separate from outcomes. A failure is
not an outcome of the work, it is the absence of one, and merging them hides
the case where a caller saw success and the work silently never completed.

**Expected outcomes are not errors.** Not-found is tagged, not failed.

**Logs are structured, and property names are attribute keys.** This is the
one that surprises people:

```csharp
logger.LogInformation("Screened {application.id}", id);    // queryable, and kept
logger.LogInformation("Screened {ApplicationId}", id);     // queryable, and dropped
logger.LogInformation($"Screened {id}");                   // banned (ADR-0004)
```

The middle line is the trap. A structured log property **is** an attribute
key, and the allowlist matches declared keys exactly
([ADR-0028](../adr/0028-logs-are-enforced-in-process-on-net10-only.md)).
`{ApplicationId}` matches no family and no declaration, so it is dropped
before export — silently, which is exactly what an allowlist is for. Name
log properties the way you would name a span attribute.

The message body itself is not an attribute and cannot be filtered by key.
Keep identifiers in properties, not in the sentence.

Interpolation is banned by [ADR-0004](../adr/0004-free-text-telemetry-and-exceptions.md)
— unqueryable and unredactable after the fact. Be aware that this one is
**not enforced at build**: there is no analyzer rule for it and `CA2254` is
not enabled here. The collector scans for it. Treat it as your discipline,
not the compiler's.

### A9. The KYC policy layer, if applicable

Only if your domain is KYC. The methods are static and set the tag on the
current span:

```csharp
KycTelemetry.SetApplicationId(applicationId);    // Class 2
KycTelemetry.SetCorrelationId(correlationId);
KycTelemetry.SetCausationId(message.MessageId);  // on consume
```

There is deliberately **no metric equivalent** of `SetApplicationId`. A
per-application dimension is a memory leak with a dashboard.

Data classes 3 (restricted PII) and 4 (secrets) appear **nowhere** in
telemetry — not spans, not logs, not metrics. If you are about to tag
something that might be class 3 or 4, stop and read
[`docs/allowlist.md`](../allowlist.md) first. Declaring a class 3 or 4 key is
ignored rather than honoured; it is not a route to emitting one.

### A10 — when the build talks back

```
warning RKS001: Attribute key 'screening.outcome' is not allowlisted and will
be dropped before export.
```

This is not a lint complaint. It is the build telling you the tag you just
wrote reaches no store — and that without the diagnostic you would find out
during an incident, from a query that returns nothing.

Three diagnostics exist:

| Id | Severity | Means |
|---|---|---|
| **RKS001** | Warning | Undeclared attribute key. It will be dropped. |
| **RKS002** | **Error** | Class 2 identifier used as a metric dimension. Unbounded cardinality degrades the store for every other service, so this one is not a judgement call. |
| **RKS003** | Warning | Exporter configured by hand. That pipeline has no allowlist processor and no governed defaults. |

Do not suppress any of them. RKS001 is not protecting a style rule; it is
telling you the data will not arrive.

Two limits worth knowing: the analyzer only sees **literal** keys, so a key
built at run time compiles clean and is still dropped at run time; and there
is **no analyzer rule for log properties**, so a misnamed `{ApplicationId}`
builds clean too. You learn about that one from the dropped-key metric, not
the compiler.

### A11 — declaring a new key

Two ways to clear RKS001, and usually only one is right.

**1. Use a key inside an allowed family.** If semantic conventions already
cover what you are tagging (`http.`, `db.`, `messaging.`, `server.`,
`url.`, `exception.`, `code.`, `user_agent.` and so on), use the
conventional key. Free, no release.

**2. Declare it in a policy pack.** Domain vocabulary — outcomes, statuses,
the infrastructure a call addressed — is declared individually, never as a
family ([ADR-0025](../adr/0025-domain-attributes-are-declared-not-a-family.md)):

```csharp
// src/Raksawi.Observability.Kyc/AssemblyInfo.cs
[assembly: AllowedAttributeKey("screening.outcome", DataClass.Infrastructure)]
```

Pick the class honestly. This is a **package release**, not a file edit, and
that is deliberate ([ADR-0017](../adr/0017-allowlist-declared-as-assembly-attributes.md)):
a vocabulary change goes through the same review as any other schema change.
Only assemblies in the closed, strong-named set may declare a key at all.

A family allow would let every future key under that prefix through all
three enforcement points without review, which is the default-deny the
allowlist exists to keep.

### A12 — verify, in this order

Do not skip to the end. Each step isolates a different failure.

1. **Collector logs show spans arriving.** Port 4318 (4319 on this repo's
   demo compose). If nothing arrives, it is the endpoint or the firewall,
   not your code.
2. **Your service alone reaches the backend**, end to end.
3. **Two services on one trace** — HTTP context propagation works.
4. **A message hop lands on the same trace.** Most likely step to fail; A7
   is the concrete failure mode.
5. **A redacted field reads correctly on a real span** — `url.full` shows the
   redacted path, and no credentials leaked through either. Redaction fails
   open, so this is verification, not a formality.
6. **Search by `correlation.id` and get the whole workflow back.**

Then check what did *not* arrive: query for one attribute you expect and one
log property you expect. A dropped key is silent by design, and the
dropped-key metric (`attribute.key`, `telemetry.signal`) is where it shows
up.

Emitting is not the end of the route. Continue at
[`backend-and-dashboards.md`](./backend-and-dashboards.md), which stands the
store up and builds the four dashboards from
[`../diagnostic-queries.md`](../diagnostic-queries.md) — and says which panels
cannot be built yet, and why.

---

## Part B — .NET Framework 4.8, SDK path

Everything in Part A about conventions, correlation, the policy layer and
the analyzer applies unchanged. The wiring differs, and **logs differ**.

Three touchpoints the library cannot reach from inside:

**1. W3C trace context, forced as the first statements of
`Application_Start`.** The 4.8 default is `Hierarchical` and the failure is
silent — a trace splits in two rather than erroring. `Start()` does this for
you, but only if it runs early enough.

**2. The returned handle held for the application lifetime**, disposed in
`Application_End`.

**3. `TelemetryHttpModule` registered in `Web.config`**, IIS in integrated
pipeline mode.

```csharp
private static RaksawiObservabilityHandle _observability;

protected void Application_Start()
{
    _observability = RaksawiObservability.Start(o =>
    {
        o.ServiceName = "my-service";
        o.ServiceNamespace = "my-domain";
        o.SamplingRatio = 0.1;
        o.ActivitySources.Add(MyTelemetry.ActivitySourceName);
        o.Meters.Add(MyTelemetry.MeterName);
    });

    // Non-null means the trace format had to be corrected. Surfaced, not
    // logged — no logging abstraction is assumed on this runtime — so write
    // it wherever this application already writes startup diagnostics.
    if (_observability.W3CWarning != null) { /* write it somewhere visible */ }
}

protected void Application_End() => _observability?.Dispose();
```

If `W3CWarning` appears after an `Activity` already exists, traces have
already split.

**Logs get one enforcement point here, not two.** There is no in-process log
allowlist on 4.8 — the collector filters log records for this runtime, and
nothing filters them before they leave the process
([ADR-0028](../adr/0028-logs-are-enforced-in-process-on-net10-only.md), a
stated deviation from Rev 3 I3.2). Property naming from A8 still applies;
you just find out later.

**This path is unvalidated until Phase 2**
([ADR-0005](../adr/0005-enforcing-the-framework-wiring.md), deferred by
[ADR-0022](../adr/0022-demo-first-resequencing.md)) — the documented 4.8
failure modes are not yet reproduced against a fixture, and CouchDB
redaction is verified on .NET 10 only. Treat it as provisional.

---

## Part C — .NET Framework 4.8, agent path

You write no code. Infra installs the auto-instrumentation MSI on the host,
runs `Register-OpenTelemetryForIIS`, and sets environment variables per
application pool. See [`infra.md`](./infra.md).

Governance for these services is applied **at the collector only**,
fail-closed ([ADR-0009](../adr/0009-governing-agent-instrumented-services.md)).
One of the three enforcement points, not three. That is a deliberate
trade — a process containing none of our code still cannot ship an
undeclared attribute, but it also gets no build-time warning and no
in-process filter.

Consequence for you: an attribute you expect and never see was dropped at
the collector, and nothing told you at build time. Check
[`docs/allowlist.md`](../allowlist.md) first.

---

## Verifying against this repo

If you are reproducing the reference behaviour locally, the scripts here are
the evidence — not "it compiles":

| Script | Proves |
|---|---|
| `.claude/scripts/check.sh` | Both target frameworks build, formatting holds |
| `.claude/scripts/test-fast.sh` | Unit tests. No collector, store or network |
| `.claude/scripts/test-full.sh` | The above plus `otelcol validate` |
| `.claude/scripts/contract.sh` | Collector policy and the declared allowlist express the same rules |
| `.claude/scripts/e2e.sh` | Assertions against **received** telemetry, not configuration |
| `.claude/scripts/e2e-instrumented.sh` | The same, on telemetry the reference services actually emitted |

The last three need a running docker daemon and **fail** rather than skip
without one, by design.

---

## When it does not work

| Symptom | Cause |
|---|---|
| Your spans emit nothing, no error | `ActivitySource` not registered (A4) |
| Your metrics never appear | `Meter` not registered (A2) |
| Nothing reaches the collector | Wrong port — 4318, not 4317; 4319 on this repo's demo compose |
| Boot fails citing `SamplingRatio` | ADR-0010 working as intended. Set it |
| `NatsException` on publish | Missing `SerializerRegistry` (A7) |
| CouchDB 401s | `HttpClient` dropped the userinfo; explicit header (A6) |
| Trace splits across a NATS hop | Consumer span not extracting parent context (A7) |
| Trace splits in two on 4.8 | W3C format forced too late; check `Application_Start` ordering (Part B) |
| A log property is missing | Named `{PascalCase}` instead of `{dotted.key}` (A8) |
| A span attribute is missing | Undeclared key, dropped. Check the build for RKS001 (A10) |
| A resource attribute is missing | Outside the narrower resource families (ADR-0026) |

Anything policy or allowlist related: [`docs/allowlist.md`](../allowlist.md),
then ADR-0002 / 0003 / 0017 / 0018 / 0025 / 0026 / 0028.

## Next

[`backend-and-dashboards.md`](./backend-and-dashboards.md) continues the route
past A12: SigNoz up, the collector alongside it, then the four dashboards built
from [`../diagnostic-queries.md`](../diagnostic-queries.md) — including an
honest account of which panels have no data source yet.

`samples/README.md` explains *why* each pattern above exists, and the
Screening service runs the whole flow — `POST /applications` to
screening-api to CouchDB, NATS publish to screening-worker, retry three
times, abandon — with fault injection to exercise verification steps 3
through 6. Its fault-injection block, sampling at 1.0 and compose
credentials are explicitly not production-shaped
([ADR-0022](../adr/0022-demo-first-resequencing.md)); do not copy those.
