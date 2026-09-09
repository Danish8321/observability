Status: open — needs a decision, not implementation

# Four of dashboard 3's six panels need the reference services on JetStream

`ScreeningConsumer.cs:32` subscribes with `nats.SubscribeAsync` — **core NATS**.
No stream, no durable consumer, no persistence, no redelivery. The class comment
says so and calls it "deliberate for the demo and wrong for production".

Core NATS has no backlog. There is nothing to be behind on: a message with no
live subscriber is dropped at the server, not queued. So the four panels that
describe a queue describe something the sample does not have:

| Panel | Asks for |
|---|---|
| 3.1 | Oldest unprocessed message age |
| 3.2 | Consumer lag / backlog depth |
| 3.3 | DLQ depth and arrival rate |
| 3.6 | What is in the DLQ, by workflow |

Confirmed empirically, not inferred. A `prometheus-nats-exporter` scrape of the
running demo with `-jsz=all` returns:

```
jetstream_server_total_streams 0
jetstream_server_total_consumers 0
```

and emits no `jetstream_stream_*` or `jetstream_consumer_*` series at all.

## Impact

Dashboard 3 exists for Rev 3 **D3.6** — the system showing perfect HTTP latency,
zero errors and 30% CPU while screening runs twenty minutes behind. It is the
dashboard that most needs to exist, and four of its six panels cannot be built
against the reference implementation.

Worse than unbuildable: it is *substitutable*. `gnatsd_varz_slow_consumers`
arrives now (ADR-0030) and looks like an answer. It counts connections the
server already disconnected for not keeping up — a number of subscribers
dropped, after the data is gone, not a number of messages waiting. Panel it as
3.2 and the twenty-minute backlog reads as a flat zero. The same substitution
was already refused for DLQ depth against
`screening.applications.abandoned`; this one is more tempting because the metric
name contains the word "consumer".

## Why this is a decision and not a task

[ADR-0022](../../../docs/adr/0022-demo-first-resequencing.md) made core NATS a
deliberate demo choice and `samples/README.md` explains the retry-and-abandon
flow as what the sample teaches. Moving to JetStream changes what the reference
implementation demonstrates: redelivery becomes real, so `causation.id` vs
`message.id` (ADR-0007) stops being a narrated distinction and becomes an
observable one — which is an argument *for* the move, but it is still a change
to the thing other repos are told to copy.

The scope is a vertical slice, not a flag: stream creation, a durable pull
consumer, ack/nak handling replacing the in-process retry loop, a DLQ (max
deliver + a dead-letter subject), and the instrumentation patterns in
`samples/README.md` rewritten to match. Both reference services, plus
`e2e-instrumented.sh`.

## Fix

Not started. Needs the decision above first, recorded as an ADR amending 0022
rather than a quiet change to the sample.

Once taken, the allowlist is already ready: `messaging.consumer.group.name` was
admitted ahead of use for exactly this (issue 01), so no allowlist review sits
on the critical path.

## Verification required

The panels are not the test. `e2e-instrumented.sh` must show a backlog that is
non-zero *and then drains* — a stopped worker, a published message, a
`jetstream_consumer_num_pending` above zero at the sink, then a started worker
and a return to zero. A panel wired to a metric that is always zero is
indistinguishable from a working one, which is the failure D3.6 describes
applied to its own dashboard.

## Comments
