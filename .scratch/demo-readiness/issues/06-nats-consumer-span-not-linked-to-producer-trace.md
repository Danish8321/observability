Status: closed

# Worker's "process" span starts a new root trace instead of joining the producer's trace

Confirmed live 2026-08-11, `app-3001` sent through the full happy path and
queried back from ClickHouse. Two traces exist for one workflow:

```
trace ea4248...  screening-api:  POST /applications (Server)
                                   application.load, GET, application.save, PUT
                                   kyc.applications.submitted publish (Producer)
                                   kyc.applications publish (Producer, NATS.Net's own span)
                                 screening-worker:
                                   kyc.applications receive (Consumer)   ← still trace ea4248, correctly linked

trace ca0835...  screening-worker:
                                   kyc.applications.submitted process (Consumer)  ← NEW ROOT, unlinked
                                   screen application, application.load, GET,
                                   application.save, PUT
```

NATS.Net's own auto-instrumented span (`kyc.applications receive`) correctly
joins the producer's trace. `ScreeningConsumer.HandleAsync`'s manually started
span (`kyc.applications.submitted process`, in
`samples/Screening.Worker/ScreeningConsumer.cs`) does not — everything from
that point on, including the actual screening work and both CouchDB calls, is
on a disconnected trace.

## What the code assumes, versus what happens

`ScreeningConsumer.cs`'s own doc comment:

> Nothing reads `traceparent` here. NATS.Net extracts trace context from
> message headers and links the consumer span to the producer span. **The span
> below is a child of that link, not a new root.**

That's the design intent, stated explicitly, and it does not hold. The two
CouchDB calls, the retry events, and the abandonment tag — everything the
demo's fault-injection path (`app-fail-1004`) depends on being visible on one
trace — land on the disconnected trace instead. `correlation.id` and
`causation.id` still tie the two traces together as business identifiers
(confirmed present and matching on both), so a workflow is still
reconstructable by searching on `correlation.id` — but not by following a
single trace, which is what `samples/README.md`'s verification step 4 ("NATS
hop on the same trace") and step 6 ("search by correlation.id and get the
whole workflow") both describe as the demo's actual point.

## Likely cause, not yet confirmed

`ScreeningConsumer.ExecuteAsync` iterates `nats.SubscribeAsync<T>(...)` with
`await foreach`, then calls `HandleAsync`, which starts the manual
`ScreeningTelemetry.Source.StartActivity(..., ActivityKind.Consumer)` with no
explicit parent context or link. If NATS.Net's automatic propagation sets
`Activity.Current` (or an equivalent ambient signal) only for the duration of
producing each item from the async-enumerable — and that scope has already
ended by the time the loop body runs `HandleAsync` — the manually started
activity has nothing to attach to and becomes a new root. This is a guess
based on the shape of the failure, not confirmed by reading NATS.Net's source;
needs the `diagnosing-bugs` treatment before a fix is written, not a
one-line patch.

## Impact

This is exactly the risk `samples/README.md` flags as "most likely step to
fail" in the verify-the-wiring checklist. It's silent — no exception, no log,
both spans export successfully — so it would not be caught by anything short
of what this session did: querying the store directly and checking trace
continuity, not just presence.

## Root cause, confirmed

The likely-cause guess above was correct in shape. NATS.Net ships a public
extraction API for exactly this —
`NatsMsgTelemetryExtensions.GetActivityContext(NatsHeaders)`, in
`NATS.Client.Core` — which reads `traceparent`/`tracestate` out of the
message headers into an `ActivityContext`. `ScreeningConsumer.HandleAsync`
never called it, and never had access to the message's headers at all — it
was only handed `message.Data`, not the `NatsMsg<T>` itself. Confirmed via
reflection against the installed `NATS.Client.Core` 3.1.0 package
(`NatsMsg<T>.Headers`, `NatsMsgTelemetryExtensions`).

`Activity.Current` was never going to carry the link either way: NATS.Net's
own auto-span for the receive lives only for the duration of producing one
item off `SubscribeAsync`'s async-enumerable, which has ended by the time the
`await foreach` body runs `HandleAsync`.

## Fixed (2026-08-11)

`ExecuteAsync` now passes the full `NatsMsg<ApplicationSubmitted>` into
`HandleAsync` instead of just `.Data`. `HandleAsync` extracts the parent
context via `NatsMsgTelemetryExtensions.GetActivityContext(message.Headers)`
and passes it explicitly to
`ScreeningTelemetry.Source.StartActivity(name, ActivityKind.Consumer,
parentContext)`.

Verified: `check.sh` clean, `test-fast.sh` 35/35. Live: fresh request
(`app-4001`), all 14 spans across `screening-api` and `screening-worker` —
including `kyc.applications.submitted process` and everything downstream of
it — share one `traceID` in ClickHouse. The doc comment's stated invariant
now holds.

## Comments
