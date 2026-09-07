Status: deferred by design — 2026-09-07

# `AllowlistProcessor` knows about CouchDB — a named product inside the mechanism layer

`AllowlistProcessor` takes `IReadOnlyCollection<string> couchDbHosts`
(`AllowlistProcessor.cs:34-38`) and owns `IsCouchDbSpan`
(`AllowlistProcessor.cs:85-113`). `AttributeAllowlist.IsAllowed` and
`AllowlistRules.IsAllowedByFamily` both carry an `isCouchDbSpan` parameter
through their signatures.

ADR-0001 defines the mechanism layer as knowing "how telemetry is produced and
shipped and nothing about any business domain". A specific database product is
not business domain, so this is not a violation of the letter — but it is a
named external technology threaded through four signatures in the layer whose
selling point is that it is generic.

## Impact

Low today. One conditional pair exists (`AllowlistRules.CouchDbOnlyKeys` =
`url.full`, `url.query`) and the coupling is explicit rather than hidden. Cost
is paid on adding a second conditional scope: a second boolean parameter
through the same four signatures, then a third.

Deliberately **not** proposing an abstraction now — YAGNI, and the current
shape is the simplest thing that works. This ticket records the seam so the
next person adding a conditional key sees it rather than adding
`isSqlServerSpan` next to `isCouchDbSpan`.

## Fix (when a second conditional scope arrives, not before)

Replace the boolean with a predicate supplied by the entry point:

```csharp
public AllowlistProcessor(AttributeAllowlist allowlist, Func<Activity, bool> isConditionalScope)
```

or a small `SpanScope` flags value if more than one conditional dimension is
needed. `AllowlistRules` then names scopes rather than products, and the
CouchDB host list stays where it belongs — in the entry point, next to the
other CouchDB wiring.

Blocked on: a second conditional key existing. Close as won't-do if the
allowlist stays at one.

## Resolution (2026-09-07) — deferred, on the ticket's own terms

Closing as won't-do-yet, per "Blocked on: a second conditional key existing".
The allowlist still has exactly one conditional pair (`url.full`, `url.query`),
so the predicate refactor would add an indirection with one implementation —
YAGNI, and the current shape is readable.

One thing did change since the ticket was written. `RaksawiPipeline` now owns
`TryRedactCouchDbUrl` and `ShouldTrace` (ticket 06), so *most* CouchDB knowledge
in the mechanism layer is already concentrated in one file next to the rest of
the CouchDB wiring. What remains in `AllowlistProcessor` is the `couchDbHosts`
constructor parameter and `IsCouchDbSpan`, threaded as `isCouchDbSpan` through
`AttributeAllowlist.IsAllowed` and `AllowlistRules.IsAllowedByFamily`.

The seam stands as documented: when a second conditional scope arrives, replace
the boolean with `Func<Activity, bool>` (or a `SpanScope` flags value) supplied
by the entry point — do not add `isSqlServerSpan` beside `isCouchDbSpan`.

Reopen when that second scope is proposed, not before.

## Comments
