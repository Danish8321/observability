Status: closed — 2026-09-07

# The allowlist's dropped-key counter breaks the exact rule RKS002 enforces

`src/Raksawi.Observability/AllowlistProcessor.cs:70`:

```csharp
DroppedKeys.Add(1, new KeyValuePair<string, object?>("attribute.key", key));
```

The dimension is the full attribute key, and the keys reaching this line are by
definition the ones **no policy pack declared and no family covers** — i.e. the
ungoverned ones. A service tagging `$"app.{applicationId}"`, or any third-party
instrumentation minting keys from data, produces one time series per distinct
value.

## Impact

`Diagnostics.ClassTwoAsMetricDimension` (RKS002,
`src/Raksawi.Observability.Analyzers/Diagnostics.cs:44-58`) is an **error**, not
a warning, on precisely this shape, and its description reads: "A per-identifier
dimension is a memory leak with a dashboard: metrics pre-aggregate per unique
dimension combination, so an unbounded dimension produces unbounded series."

The component enforcing that rule violates it. RKS002 cannot catch it — the
analyzer only sees string literals, and `key` here is a variable.

Currently latent because nothing subscribes the meter (issue 01). Fixing 01
without fixing this ships the cardinality problem to the metrics store.

## Fix

Bound the dimension. Two options, either acceptable:

- **Family prefix.** Dimension by the key up to its first `.` (`"app"`,
  `"http"`, `"screening"`), with a `{other}` bucket for keys with no dot. The
  question the counter exists to answer — "which family failed to cover a real
  key" — is answered at family granularity, and the family table is finite.
- **Capped distinct set.** Emit the full key for the first N distinct keys seen
  and roll everything after into `attribute.key="{other}"`. Keeps full fidelity
  in the normal case; needs a bounded concurrent set on the export path.

Prefer the first: no per-span state, no allocation, no cap to tune.

Whichever is chosen, state the bound in the XML doc so the next reader does not
re-add the raw key.

## Verification required

A unit test that drops N synthetic keys sharing a prefix and asserts the
listener observed a bounded number of distinct dimension values (not N).

## Fixed (2026-09-07)

Capped distinct set, not the family prefix this ticket first preferred.

**Why the prefix was rejected.** Truncating to the first segment is only
bounded when the varying part is *not* the first segment. A key built as
`$"{applicationId}.state"` truncates to one family per application — the same
unbounded series, now harder to spot. A cap is bounded whatever the key looks
like.

`AllowlistProcessor.DimensionFor` admits the first
`MaxDistinctDroppedKeys` (100) distinct keys as themselves and reports
everything after as `OverflowDimension` (`{other}`). The set is a
`ConcurrentDictionary` per processor, not static: a provider has one processor,
so the bound holds, and a test can reach the cap without depending on what other
tests dropped first. Check-then-add is deliberately not atomic — the bound is
cap + concurrency rather than exactly cap, and a lock on the export path buys
nothing (documented at the method).

Full-fidelity keys survive in the normal case, which is a handful of undeclared
keys. That was the diagnostic value the counter exists for.

## Verified

`AllowlistMetricsTests.The_dimension_is_bounded_when_a_service_builds_keys_from_data`
drops 1000 distinct generated keys through one processor and asserts the
distinct dimension values it produced are ≤ 100, with `{other}` present.

First run failed at `103 distinct dimension values from 1000 distinct keys` —
the meter is process-wide and static, so the listener also saw drops from tests
running in parallel. Assertion now filters to a prefix unique to the test run.
The 100 + overflow bound itself was correct on the first run.

`check.sh` clean. `test-fast.sh` 139/139.

## Comments
