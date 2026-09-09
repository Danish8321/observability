Status: closed — 2026-09-08

# The unbounded-dimension rule protected `message.id`, which nothing emits, while `messaging.message.id` travelled unprotected

Rev 3 **D2.1 rule 1**: Class 2 is permitted on spans and logs and refused as a
metric dimension. `AllowlistRules.NeverAMetricDimension` and the collector's
`datapoint` delete both listed `message.id`.

`message.id` is a **declared** Class 2 key in this repo's own correlation model
(ADR-0007) and is emitted by nothing. The spelling that actually travels is
`messaging.message.id` — the OTel semantic convention key, set by the NATS
instrumentation and by `ScreeningConsumer.cs:73`. It was in neither list.

Found by reading a sink dump for issue 01, not by a gate. The two names differ
by one prefix and the rule looked correct at a glance in both places.

## Impact

A per-message identifier is unbounded by construction. Admitted as a metric
dimension it is a new time series per message — the "memory leak with a
dashboard" the KYC policy layer refuses `application.id` for
(`KycTelemetry.SetApplicationId` tags spans only, deliberately with no metric
equivalent).

Nothing in the sample dimensioned a metric by it, so no cardinality incident
occurred. What was absent was the guard that stops one, on the only spelling
that can cause one.

## Fix

`messaging.message.id` added to `NeverAMetricDimension` and to the collector's
`datapoint` delete alongside `message.id`. Both kept: `message.id` stays because
the correlation model declares it and something may yet emit it.

## Verification

`e2e-instrumented.sh` asserts absence scoped to a datapoint rather than to the
file, because the key is legitimately present on a span in the same export:

```
  ok      absent  messaging.message.id as a metric dimension
```

## Comments
