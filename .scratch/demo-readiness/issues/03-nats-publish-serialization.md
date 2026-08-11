Status: closed

# ApplicationPublisher.PublishAsync throws — NATS client has no JSON serializer registered

`samples/Screening.Domain/ApplicationPublisher.cs:52` calls
`nats.PublishAsync(Subject, message, ...)` with `message` typed
`ApplicationSubmitted` (a record). Every call throws:

```
NATS.Client.Core.NatsException: Can't serialize Screening.Domain.ApplicationSubmitted
   at NATS.Client.Core.NatsUtf8PrimitivesSerializer`1.Serialize(...)
```

`NatsUtf8PrimitivesSerializer` is the default when no serializer is
registered — it only handles primitives/strings, not POCOs. Both
`Screening.Api/Program.cs:30-33` and `Screening.Worker/Program.cs:22-25`
construct `NatsConnection` with a bare `NatsOpts { Url = ... }` — no
`SerializerRegistry` set, so `NatsClient.Net` never picks up a JSON
serializer.

## Impact

Every `POST /applications` (any application ID) 500s at the publish step,
after the CouchDB document is already written — the caller sees an error but
the record exists as `status: received` with no consumer ever notified. This
is the exact "202 sat in the caller's log, work silently never completes"
failure the demo's abandonment path (`app-fail-1004`) is supposed to
*demonstrate deliberately* — here it happens unintentionally on every
request, including the happy path.

## Fixed (2026-08-11)

Registered `SerializerRegistry = NatsJsonSerializerRegistry.Default`
(package `NATS.Client.Serializers.Json`, already referenced) on `NatsOpts` in
both `Screening.Api/Program.cs` and `Screening.Worker/Program.cs`. Confirmed
against `NATS.Net` 3.1.0, the version pinned in both `.csproj` files.

Verified live: full happy path run end to end — `POST /applications`
(`app-1001`) → 202 → worker consumes → CouchDB updated. `GET
/applications/app-1001` returned `status: screened, outcome: clear`. The
NATS hop and worker-side spans now exist; issue 01's verification only
reached the API's own CouchDB spans, this closes the gap. `check.sh` clean.

## Comments
