# Onboarding: integrating a service

For teams adding `Raksawi.Observability` (and optionally `Raksawi.Observability.Kyc`) to an estate service. Not for work on this repo itself — see [`developer.md`](./developer.md) for that.

## Prereqs

- Package comes from Azure Artifacts, not this repo. This repo has no service code to copy.
- Know your path: **.NET 10** (SDK, `IHostApplicationBuilder`), **.NET Framework 4.8 SDK path** (package + manual touchpoints), or **.NET Framework 4.8 agent path** (no package, MSI-based, governed at collector — see [ADR-0009](../adr/0009-governing-agent-instrumented-services.md)). Ask your platform contact if unsure which applies.

## .NET 10 path

One call:

```csharp
builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "my-service";        // bare, kebab-case (ADR-0006) — never a hostname
    o.ServiceNamespace = "my-domain";
    o.SamplingRatio = 0.1;               // required outside Development (ADR-0010)
    o.ActivitySources.Add(MyTelemetry.ActivitySourceName);  // unregistered sources emit nothing — #1 wiring mistake
    o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);            // only if you talk to CouchDB — see redaction below
});
```

Set env vars: `OTEL_SERVICE_NAME`, `DEPLOYMENT_ENVIRONMENT`. Optionally `OTEL_SERVICE_INSTANCE_ID` (stable across app pool/process recycle — derived from machine name if absent, ADR-0008).

`OtlpEndpoint` defaults to `http://localhost:4318` — http/protobuf, **not** gRPC (4317 closed estate-wide, unsupported on 4.8). Point it at your collector.

Boot fails fast (not silently) if `ServiceName`/`ServiceNamespace` are missing, or `SamplingRatio` is absent outside Development — these are config errors, never telemetry outages (Rev 3 I3.6: telemetry setup never fails a service *for telemetry reasons*, but a missing sampler is a real misconfiguration, not a telemetry failure).

## .NET Framework 4.8, SDK path

Package reference plus three touchpoints the library cannot reach from inside:

1. `Activity.DefaultIdFormat`/`ForceDefaultIdFormat` forced to W3C as the **first statements** of `Application_Start` — default on 4.8 is Hierarchical and the failure is silent (a trace splits in two, no error). `RaksawiObservability.Start()` does this for you, but only if called early enough.
2. The returned `IDisposable` held for the application lifetime, disposed in `Application_End`.
3. `TelemetryHttpModule` registered in `Web.config`, IIS in integrated pipeline mode.

```csharp
private static IDisposable _observability;

protected void Application_Start()
{
    _observability = RaksawiObservability.Start(o =>
    {
        o.ServiceName = "my-service";
        o.ServiceNamespace = "my-domain";
        o.SamplingRatio = 0.1;
    });
}

protected void Application_End() => _observability?.Dispose();
```

Check `Handle.W3CWarning` (surfaced, not logged — no logging abstraction assumed on this runtime) after `Start()` if you need to confirm the correction happened before the first `Activity`.

**Unvalidated until Phase 2** ([ADR-0005](../adr/0005-enforcing-the-framework-wiring.md), deferred by [ADR-0022](../adr/0022-demo-first-resequencing.md)) — the three documented 4.8 failure modes aren't yet reproduced against a fixture. Treat this path as provisional until that lands.

## .NET Framework 4.8, agent path

No package. Auto-instrumentation MSI on the host + `Register-OpenTelemetryForIIS` + env vars per application pool. You do nothing in code; governance is applied at the collector, fail-closed, per [ADR-0009](../adr/0009-governing-agent-instrumented-services.md). Talk to platform/infra to get the host set up — see [`infra.md`](./infra.md).

## KYC / policy layer

If your domain is KYC, also reference `Raksawi.Observability.Kyc`:

```csharp
activity?.SetApplicationId(applicationId);   // Class 2 — spans only, never a metric dimension by design
```

Data classes 3 (restricted PII) and 4 (secrets) appear **nowhere** in telemetry — not spans, logs, or metrics. If you're about to tag something that looks like it might be class 3/4, stop and check [`docs/allowlist.md`](../allowlist.md) first; the analyzer will also catch unknown keys at build.

## Correlation — mint or continue it

Four business identifiers, four different lifetimes (see [`CONTEXT.md`](../../CONTEXT.md#correlation)):

| Identifier | Lifetime | You do |
|---|---|---|
| `trace_id` | one request | nothing — SDK |
| `session.id` | one browser page-load | continue from `X-Correlation-Id` if the caller sent one |
| `correlation.id` | one business workflow | mint at workflow start if none supplied; echo it back in a response header |
| `causation.id` | direct parent message | set on consume, from the message that caused this work |

See `samples/Screening.Api/CorrelationMiddleware.cs` for a working example.

## Conventions you must follow

- Structured logs only — `LogInformation("Screened {ApplicationId}", id)`, never `$"Screened {id}"`. Interpolation is banned ([ADR-0004](../adr/0004-free-text-telemetry-and-exceptions.md)) and scanned for at the collector.
- Span names describe the work ("screen application"), not the method name.
- `ActivityKind.Producer`/`Consumer` on both sides of a message hop, or the two spans don't render as a hop.
- Retry is an event (`AddEvent`), not an error status — reserve `SetStatus(Error)` for actually giving up. Otherwise your error rate becomes meaningless and gets ignored.
- Expected outcomes (e.g. not-found) are not errors — tag them, don't fail them.
- CouchDB (or any HTTP dependency exposing IDs in the URL) needs `CouchDbHosts` + `RedactCouchDbUrls` (on by default). **Exact host match, fails open** — verify redaction on a real span before trusting it in a shared environment.

## Verify before trusting it

1. Collector logs show received spans (port **4318**, not 4317)
2. Your service alone reaches SigNoz/backend
3. Two services on one trace — HTTP propagation works
4. A message hop (NATS etc.) on the same trace — **most likely step to fail**
5. Any redacted field reads correctly (e.g. `url.full` shows `/kyc/{docid}` redacted)
6. Search by `correlation.id` and get the whole workflow back

Full worked example, including fault injection to exercise steps 3-6: `samples/README.md` and the Screening reference service (`samples/Screening.Api`, `.Domain`, `.Worker`).

## Getting stuck

- Unregistered `ActivitySource` → emits nothing, no error. Check `ActivitySources.Add(...)` first.
- Wrong collector port → check 4318 not 4317.
- Trace splits in two on 4.8 → W3C format forced too late; check `Application_Start` ordering.
- Anything policy/allowlist-related → `docs/allowlist.md`, then the relevant ADR (0002/0003/0017/0018).
