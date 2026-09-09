Status: open

# The persistent queue's disk is unmeasured, while its size is still an unverified placeholder

`deploy/collector/config.yaml` gives the SigNoz exporter a `sending_queue` with
`storage: file_storage`, `queue_size: 10000`, and this comment:

> Sized from a measured restore window at I3.6/I3.7. This is a demo placeholder
> and is explicitly not the durability bound (Q13).

The `file_storage` extension publishes **no metrics**. Confirmed against a
running 0.140.1: the self-scrape endpoint carries `otelcol_exporter_queue_size`
(items) and nothing about the volume behind it. Panel 4.6 asks for "collector
memory, ingestion, disk"; the first two arrive and the third has no source.

## Impact

Two separate consequences, and the second is the one that bites.

**The panel is partial.** 4.6 shows memory and ingestion, not disk.

**Q13 cannot be answered from telemetry.** The open question is how long the
collector can hold data while the store is down. `queue_size: 10000` is a count
of *items*, not bytes, and items vary in size by orders of magnitude between a
batch of spans and a batch of metric datapoints. Nothing reports how much disk
10000 items actually consumed, so:

- the placeholder cannot be replaced by a measured value, because the
  measurement does not exist;
- the volume filling is invisible until the collector fails to write, at which
  point the durability guarantee has already failed;
- `/var/lib/otelcol/storage` is a docker volume with no size limit in the demo,
  so on a host with a real disk this is the failure that takes the host with it.

Rev 3 **I3.6** — "telemetry must never participate in business request success"
— is what the queue implements. An unmeasured queue is that guarantee held on
trust.

## Fix

Not from the collector. Two candidates:

1. **`hostmetrics` receiver, `filesystem` scraper**, mounted on the storage
   path. Gives used/free bytes for the volume. It reports the *filesystem*, not
   the queue's share of it, which for a dedicated volume is the same number and
   for a shared one is not — so the volume must stay dedicated for the metric to
   mean what the panel says.
2. Upstream: `file_storage` exposing its own size. Correct, absent, not on this
   repo's path.

Option 1, with the dedicated-volume requirement written down where someone
deploying this will read it — otherwise the panel silently changes meaning the
first time the volume is shared.

🔒 A `hostmetrics` receiver introduces host-level attributes into the pipeline.
`host.` is an allowed resource family (ADR-0026) but the datapoint attributes it
brings (`device`, `mountpoint`, `type`, `state`) are in no family and no
declaration. Dump before wiring, per ADR-0018 — the same enumeration issue 01
existed for.

## Verification required

Fill the queue and watch the number move: hold the export target down until the
volume grows measurably, confirm the metric tracks it, restore the target and
confirm it drains. Then the answer to Q13 is bytes-per-item times the queue
size, measured, and the placeholder comment can be replaced by a figure.

## Comments
