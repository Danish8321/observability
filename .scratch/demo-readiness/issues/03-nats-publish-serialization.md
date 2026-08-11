Status: open

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

## What to do

Register a JSON serializer on `NatsOpts` in both `Screening.Api/Program.cs`
and `Screening.Worker/Program.cs`, e.g.:

```csharp
new NatsConnection(new NatsOpts
{
    Url = ...,
    SerializerRegistry = NatsJsonSerializerRegistry.Default,
})
```

(Confirm exact API against the `NATS.Net` version pinned in
`Directory.Packages.props` — the registry/serializer surface has moved
between major versions.) Then re-run the full happy path end to end: `POST
/applications` → worker consumes → CouchDB updated to `screened` — to confirm
the NATS hop and worker-side spans actually appear on the trace, which issue
01's verification never reached.

## Comments
