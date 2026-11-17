# Slow consumer

**Means:** in the last ten minutes the NATS server disconnected at least one
subscriber for not reading: its pending data passed the server's write deadline
(10 s by default) or `max_pending` (64 MB). The messages the server held for
it are gone; core NATS does not redeliver. The subscriber reconnects and carries
on, so the service looks healthy afterwards.

This is not lag. A NATS.Net subscriber that falls behind keeps reading the
socket and drops the newest messages in the client instead (`DropNewest`, the
default), and this alert never fires for it. "Delivery gap" catches that case.

**First query** (Mimir): disconnections per broker in the last ten minutes. The
metric has no per-client label, so it says when and where, not who.

```promql
sum by (deployment_environment_name, instance) (increase(gnatsd_varz_slow_consumers{job="nats"}[10m]))
```

Then who: the server keeps recently closed connections, with the reason and
the subjects they subscribed to. On the broker's monitoring port:

```sh
curl -s "http://<nats-host>:8222/connz?state=closed&subs=1"
```

Look for `"reason": "Slow Consumer (Write Deadline)"` or
`"Slow Consumer (Max Pending)"`. `subscriptions_list` names the subjects, `ip`
the host, and `name` the client if it set one. The broker's log has the same
line: `Slow Consumer Detected`.

**Likely causes**

- A subscriber stalled: blocked on a lock, paused by GC, or its host is short
  of CPU.
- A burst on a subject arrived faster than one subscriber can read, and that
  subscriber is not in a queue group that shares the load.
- The network to that subscriber is slow.

**Owner:** the team that owns the disconnected subscriber, found above. Infra
owns the broker and its limits.
