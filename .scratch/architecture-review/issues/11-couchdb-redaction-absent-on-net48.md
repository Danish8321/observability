Status: blocked — .NET 10 half verified 2026-09-07. 4.8 deferred out of the
build entirely on 2026-09-08 (ADR-0029), so this cannot be worked until that
decision is reversed. The code below is no longer compiled by anything.

# CouchDB URL redaction and changes-feed filtering are absent on the .NET Framework 4.8 path

`src/Raksawi.Observability/RaksawiObservabilityExtensions.net10.cs:55-74` wires
both ADR-0023 controls onto HttpClient instrumentation:

```csharp
.AddHttpClientInstrumentation(http =>
{
    http.FilterHttpRequestMessage = request => ... !CouchDbUrlPolicy.IsChangesFeed(...);
    http.EnrichWithHttpRequestMessage = (activity, request) => ... activity.SetTag("url.full", CouchDbUrlPolicy.Redact(...));
})
```

`src/Raksawi.Observability/RaksawiObservability.net48.cs:55` is bare:

```csharp
.AddHttpClientInstrumentation()
```

`CouchDbUrlPolicy` is compiled into the net48 assembly and called from nowhere
on that target.

## Impact

On 4.8, for any service talking to CouchDB:

1. **Document identifiers reach the store in `url.full`.** The allowlist does
   not save this. `AllowlistRules.CouchDbOnlyKeys` allows `url.full` and
   `url.query` precisely *when the span targets a configured CouchDB host*
   (`AllowlistRules.cs:97`), on the stated grounds that "`CouchDbUrlPolicy` has
   already replaced the document identifier and view key with placeholders".
   On net48 it has not. The conditional carve-out therefore admits the raw URL —
   the one path where the allowlist is deliberately permissive is the one where
   the redaction it depends on is missing. Userinfo credentials in the CouchDB
   URL ride along too; `CouchDbUrlPolicy.Redact` is described as "the only
   backstop" for those (`CouchDbUrlPolicy.cs:38-41`).
2. **`_changes` long-poll spans are not filtered**, so spans of arbitrary
   duration corrupt every latency percentile computed from span data — the
   exact failure `IsChangesFeed` exists to prevent.

Severity is bounded today by ADR-0022 (4.8 deferred past the demo) and ADR-0012
(net48 built but unvalidated until Phase 2), so nothing in production is
affected yet. It must be closed before any 4.8 service ships, and it is not
currently recorded anywhere as an open 4.8 gap — ADR-0005 lists three
documented 4.8 failure modes and this is not one of them.

## Fix

The `OpenTelemetry.Instrumentation.Http` options surface differs by target: on
.NET Framework the hooks are the `HttpWebRequest` pair
(`FilterHttpWebRequest`, `EnrichWithHttpWebRequest`) rather than the
`HttpRequestMessage` pair used on net10, because the netfx instrumentation
hooks `HttpWebRequest`. Verify against 1.17.0 and wire the equivalents, sharing
the policy decision with net10 through the extraction proposed in issue 06 so
the two cannot drift again.

If the hooks turn out not to be available on that target, then the correct
response is to remove `url.full` / `url.query` from the conditional carve-out
for net48 — deny rather than export unredacted — and record the deviation in
ADR-0023.

## Verification required

Cannot be closed by a unit test on `CouchDbUrlPolicy` alone (already covered by
`CouchDbUrlPolicyTests.cs`). Needs a real 4.8 span against a CouchDB host,
which is Phase 2 fixture work per ADR-0005. Until then this ticket stays open
as a known gap rather than being closed on a code-reading.

## Code landed (2026-09-07)

Wired in `RaksawiObservability.net48.cs`, through the shared helpers introduced
by issue 06 so the two runtimes cannot drift again:

```csharp
.AddHttpClientInstrumentation(http =>
{
    http.FilterHttpWebRequest = request => RaksawiPipeline.ShouldTrace(request?.RequestUri);
    http.EnrichWithHttpWebRequest = (activity, request) =>
    {
        if (RaksawiPipeline.TryRedactCouchDbUrl(options, request?.RequestUri, out var redacted))
        {
            activity.SetTag("url.full", redacted);
        }
    };
})
```

**The API question this ticket raised is answered.** The hooks do exist on the
.NET Framework target of `OpenTelemetry.Instrumentation.Http` 1.17.0, but under
different names: HttpClient is instrumented at `HttpWebRequest` there, so
`FilterHttpRequestMessage` / `EnrichWithHttpRequestMessage` never fire and
`FilterHttpWebRequest` / `EnrichWithHttpWebRequest` are the equivalents.
Confirmed by compiling. So the fallback this ticket proposed — removing
`url.full` from the conditional carve-out for net48 and recording the deviation
in ADR-0023 — is not needed.

## Still open, deliberately

The verification requirement stands: `check.sh` proves it compiles on net48 and
`RaksawiPipelineTests` proves the shared policy is right, but neither proves the
netfx hook actually fires against a real CouchDB call. That needs the Phase 2
4.8 fixture (ADR-0005, deferred by ADR-0022).

Closing this on a code-reading is exactly what the repo's verification contract
forbids, so it stays open with the code in place. Whoever builds the Phase 2
fixture closes it by inspecting a stored span for `{docid}` rather than a raw
identifier, and confirming no `_changes` span is present.

## .NET 10 half verified (2026-09-07)

`.claude/scripts/e2e-instrumented.sh` runs `screening-api` and
`screening-worker` as containers against a real CouchDB, exporting through
`deploy/collector/config.yaml` unmodified, and asserts on what reached the
sink:

```
  ok      present the redacted CouchDB url.full      (couchdb:5984/kyc/{docid})
  ok      absent  the CouchDB document identifier in url.full
```

Fifteen assertions, two consecutive clean runs. That discharges the "verify
against a real span" caveat in ADR-0023 for .NET 10 — the shared policy in
`RaksawiPipeline` demonstrably fires against real HttpClient instrumentation
rather than only against a unit test Uri.

It does **not** discharge it for 4.8, which reaches the same policy through
`FilterHttpWebRequest`/`EnrichHttpWebRequest` — different instrumentation,
different code path, no fixture. Verifying one runtime and inferring the other
is the reasoning this ticket exists to refuse, so it stays open on exactly the
terms above.

The services in that script also had to reach CouchDB by container name:
the collector keeps `url.full` only when `server.address` is the CouchDB host,
so a run against `localhost` would have had the key dropped by the collector
and the assertion would have proved nothing about the library.

## Blocked by ADR-0029 (2026-09-08)

4.8 left the build. `check.sh` no longer compiles the fix recorded above, so the
one guarantee this ticket did have — that the netfx hook names are right,
confirmed by compiling — stops being re-checked on every build. The code is
behind `#if NETFRAMEWORK` and is now a record of what was written rather than
working code.

Nothing about the analysis changes. The verification requirement is unchanged
and unmet, and 4.8 resuming is the event that unblocks it.

## Comments
