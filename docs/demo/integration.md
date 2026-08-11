# Integrating the package and running the demo

For the path chosen 2026-08-10: integrate into the real application, run it end
to end on dummy data, demo that.

🔒 **Dummy data only.** That is the boundary in
[ADR-0022](../adr/0022-demo-first-resequencing.md) and it is the reason the
compliance work can be deferred. The allowlist processor does not exist yet, so
nothing stops a real CPR reaching the store if real data flows through.

---

## Step 1 — reference the package

Not on a feed yet. Two options; the first is better during a demo build because
edits take effect on rebuild.

```xml
<ProjectReference Include="..\..\observability\src\Raksawi.Observability\Raksawi.Observability.csproj" />
```

Or pack it and consume locally:

```sh
dotnet pack src/Raksawi.Observability -c Release -o ./artifacts
```

```xml
<!-- nuget.config in the consuming solution -->
<add key="local" value="../observability/artifacts" />
```

## Step 2 — wire it up (.NET 10)

```csharp
builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "screening-api";          // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "kyc";
    o.OtlpEndpoint = new Uri("http://localhost:4318");
    o.SamplingRatio = 1.0;                    // required outside Development
    o.CouchDbHosts.Add("localhost");          // whatever host CouchDB is on
    o.ActivitySources.Add("Raksawi.Screening"); // only if the app has its own
});
```

Repeat per service, changing only `ServiceName`. **Every service points at the
same collector** — that is what makes one trace span all of them.

Two failure modes worth knowing before they cost an afternoon:

- **`ServiceName` must differ per service.** Two services sharing a name merge
  into one node and the demo's whole point disappears.
- **`CouchDbHosts` must match the actual host string** used in connection URLs.
  It is a plain equality check on `Uri.Host`. Wrong value means no redaction and
  no error — it fails open.

## Step 3 — NATS

Nothing to write. `NATS.Net` ≥ 3.0.1 emits publish and subscribe spans and
propagates trace context through message headers itself; the package registers
its `NATS.Net` activity source. Do not inject `traceparent` by hand, and delete
any custom message counters — they will double-count against the built-in ones.

**Verify the version.** Below 3.0.1 there is no built-in tracing, the trace
breaks at the NATS hop, and the break is silent: two unrelated traces rather
than one error.

## Step 4 — run the stack

```sh
git clone -b main https://github.com/SigNoz/signoz.git
cd signoz/deploy/docker && docker compose up -d
```

Then the collector from [`../../deploy/collector/config.yaml`](../../deploy/collector/config.yaml).

## Step 5 — the walk-through, before trusting anything

In order. Each step fails differently, and stopping at the first failure saves
guessing later.

1. **Is the service exporting?** Collector logs show received spans. Nothing
   here means wrong endpoint or wrong port — it is 4318, not 4317
2. **Does one service appear in SigNoz?** If yes, export works end to end
3. **Do two services appear on one trace?** HTTP propagation works
4. **Does the NATS hop join the same trace?** The step most likely to fail
5. **Are CouchDB calls present, and is `url.full` redacted?** Look at an actual
   span. If document IDs are visible, `CouchDbHosts` is wrong
6. **Search by `trace_id` and get the whole path.** This is the demo

## Step 6 — make it a demo rather than a tour

Stakeholders have seen dashboards. A healthy system rendered beautifully is the
demo that already did not convince them, and running it again on dummy data is
the main risk in this approach.

**Break something on purpose, live.** Cheap — roughly an hour — and it converts
a tour into a diagnosis:

| Fault | How | What it shows |
|---|---|---|
| Downstream failure | Throw in one service's handler for a specific dummy record | The failing hop highlighted inside a trace that crosses services |
| Slow dependency | `Task.Delay` in a CouchDB call path | Where latency actually went, rather than where it was noticed |
| Silent async drop | Consumer retries and abandons | The API returned 202 and the work never completed — the case today's logs are worst at |

The third is the strongest. It is the failure where the edge looks healthy and
nothing downstream finished, and it is genuinely hard to diagnose without this.

**Answered 2026-08-11 (QD1b): the silent async drop, already coded.** Submit an
`ApplicationId` containing `"fail"`. `ScreeningService` throws
`ScreeningProviderException`; `ScreeningConsumer` retries 3×, each attempt a
`screening.retry` span event, then abandons — tagged `screening.abandoned`,
counted by reason on the `Abandoned` metric. No redeploy, no new code. `slow`
is available as a second beat if there's time.

**Say the "before" number out loud.** Roughly how long the same fault would take
to find today, across N log files with no shared identifier. Comparison is the
argument; the UI is not.

## What to say when asked "can we put this in production tomorrow"

It will be asked, and the honest answer is a short list rather than a no:

- Allowlist enforcement at the collector — the only thing stopping a CPR from
  reaching the store
- Access tiers ([ADR-0020](../adr/0020-telemetry-access-tiers.md))
- Sampling policy set from measured volume, not 1.0
- Queue sized from a measured restore window

That is roughly the eight weeks. Running it in parallel with continued build is
how it gets absorbed rather than paid twice.
