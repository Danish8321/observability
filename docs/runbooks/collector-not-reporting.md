# Collector not reporting

**Means:** the collector's own metrics have not reached Mimir for about five
minutes. The collector sends them through the same Mimir exporter as every
service metric, so this fires for two different faults:

1. **The collector is down.** Nothing reaches any store: no traces, logs or
   metrics, from any service.
2. **The collector is up, but its export to Mimir fails.** Logs and traces may
   still arrive; metrics do not, and every metric alert is blind.

If "Store unreachable: Mimir" is also firing, start there: Mimir is down and
this alert is a consequence. It is set not to fire on a failed query for that
reason. Once Mimir answers again the collector replays the missing samples, so
this may fire briefly or not at all.

**First check:** is the collector pod running?

```sh
kubectl get pods -n <namespace> -l <the gateway's selector>
```

Not running (or restarting) means cause 1. Running means cause 2: confirm in
its log, which names every failed Mimir export.

Logs arriving in Loki (`sum(count_over_time({service_name=~".+"}[5m]))`) also
point to cause 2, but no logs proves nothing: a quiet estate sends none.

**Likely causes**

- Cause 1: the pod crashed or was evicted (`kubectl describe pod`), out of
  memory (the `memory_limiter` refuses data before this, so look for an OOM
  kill), or a configuration change failed to load (the collector logs the
  failing key and exits).
- Cause 2: `MIMIR_OTLP_ENDPOINT` wrong after a change, a network policy, or
  Mimir rejecting samples. The collector logs every failed export with
  Mimir's reason; `err-mimir-sample-out-of-order` means the replayed queue is
  older than `MIMIR_OUT_OF_ORDER_WINDOW`.

**Owner:** Infra.
