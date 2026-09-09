Status: open

# Panel 4.5 reads zero during exactly the outage it exists to show

`otelcol_exporter_sent_spans_total` and
`otelcol_exporter_send_failed_spans_total` are incremented when an export
attempt **resolves**. With `retry_on_failure` enabled — which
`deploy/collector/config.yaml` sets, correctly — a failing export does not
resolve. It is retried with backoff, indefinitely.

Observed on a running 0.140.1, and it is why this is a ticket rather than a
note. Two probes against a dead exporter:

| Configuration | `sent_spans_total` | `send_failed_spans_total` |
|---|---|---|
| queue + retry enabled | absent | absent |
| queue + retry disabled | present | present |

The series do not appear at zero. They **do not appear at all** until something
resolves, so a store outage produces no failure signal and no success signal —
just two metrics that are missing.

## Impact

Panel 4.5 is "export failure rate". During a store outage — the condition it
exists for — it shows nothing, and "nothing" renders as an empty panel or a
flat zero depending on the store. An operator reading dashboard 4 during an
incident sees a healthy-looking export panel while nothing is being exported.

This is Rev 3 **I3.8** in miniature: a monitoring stack reporting healthy while
dead. The panel that was supposed to catch it is the one lying.

Absence-vs-zero also has a second edge: with no traffic at all, the same series
are also missing. "Nothing exported because nothing arrived" and "nothing
exported because the store is down" look identical on this panel.

## Fix

4.5 cannot be built on the failure counter alone. The signal that *does* move
during a retrying outage is the queue:

- `otelcol_exporter_queue_size` rising or held non-zero, with
- `otelcol_exporter_sent_spans_total` flat or absent over the same window

That is the same derived condition issue 05 needs for queue staleness, which is
not a coincidence — a stuck queue and a retrying exporter are the same event
seen from two sides. Specify it once in `docs/diagnostic-queries.md` and have
4.4 and 4.5 both reference it, rather than two panels each half-answering.

The receiver side is unaffected and stays honest:
`otelcol_receiver_accepted_spans_total` keeps rising while the export fails, so
4.3 shows data still arriving. The gap between accepted and sent *is* the
outage, and that difference is the most direct available statement of it.

## Verification required

Produce the retrying case, not the resolved one. A test against an exporter with
retries disabled proves nothing about the deployed configuration — it is what
made the difference visible in the probe above, and it is the opposite of what
ships.

## Comments

Worth stating in `deploy/README.md` alongside the four metric names already
listed there: `send_failed` is not the failure signal it reads as.

