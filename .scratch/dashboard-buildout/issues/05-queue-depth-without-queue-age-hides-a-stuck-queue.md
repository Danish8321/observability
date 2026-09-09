Status: open

# Panel 4.4 asks for queue depth *and queue age*; no age metric exists

`docs/diagnostic-queries.md` panel 4.4 is "Queue depth and queue age". The
collector's self-scrape (ADR-0031) delivers the first half —
`otelcol_exporter_queue_size` and `otelcol_exporter_queue_capacity` — and there
is no metric for the second, in 0.140.1 or any version. Confirmed against a
running collector with a queue held full against a dead exporter, not read from
documentation.

## Impact

A queue at 8000 of 10000 that is draining and refilling is healthy. A queue at
8000 that has not moved in ten minutes is an incident. **They are the same
number.** Depth alone cannot separate them, and depth is what the panel would
show.

This matters more here than in a general collector deployment because the
sending queue is the durability story: `queue_size: 10000` with `file_storage`
behind it is what makes "the collector holds data rather than pushing back on
producers" (Rev 3 I3.6) true. A stuck queue is that guarantee failing silently —
data is neither lost nor delivered, and the depth panel says "fine, 80%".

## Fix

No direct metric to wire. Three options, in preference order:

1. **Derive staleness from the pair.** `otelcol_exporter_queue_size` non-zero
   while `otelcol_exporter_sent_*` is flat over the same window is a stuck
   queue. It is a query, not a metric, so it belongs in
   `docs/diagnostic-queries.md` as a specified panel definition rather than
   in the collector config. Cheapest, and store-neutral, which 4.4 has to be
   (ADR-0016).
   🔒 Note the interaction with issue 07: while the exporter is retrying,
   `sent_*` and `send_failed_*` are *both* flat, so this condition is exactly
   what a retrying outage looks like. That is the intended reading — but it
   means 4.4 and 4.5 must be read together, and the panel definition has to say
   so.
2. Amend panel 4.4 in `docs/diagnostic-queries.md` to what is measurable and
   record why, so the specification stops describing a metric that does not
   exist.
3. Upstream: propose a queue-age or oldest-item-timestamp metric to
   opentelemetry-collector. Correct, slow, and not on this repo's critical path.

Option 1 plus option 2 together. Option 3 optionally alongside.

## Verification required

The stuck case has to be produced, not reasoned about: hold the export target
down with retries enabled, confirm depth is non-zero and flat, confirm the
derived condition fires, then restore the target and confirm it clears. A panel
verified only against a healthy collector is verified against the case it is not
for.

## Comments
