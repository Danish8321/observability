Status: closed — 2026-09-07

# Security-critical comment on `IsCouchDbSpan` labels fail-closed behaviour "fails open", and contradicts itself in the next clause

`src/Raksawi.Observability/AllowlistProcessor.cs:76-84`:

```
/// 🔒 Exact host match, and it <b>fails open</b> in the same direction
/// <see cref="CouchDbUrlPolicy"/> does: an unconfigured host is not treated
/// as CouchDB, so <c>url.full</c> is denied rather than allowed. A host
/// missing from configuration therefore loses diagnostic value, and never
/// leaks — the opposite of the redaction path's failure, deliberately.
```

Denying `url.full` on an unconfigured host is failing **closed**. The comment
labels it "fails open", then two clauses later correctly calls it "the opposite
of the redaction path's failure" — which is the same sentence asserting both
"same direction as" and "opposite of" `CouchDbUrlPolicy`.

The behaviour is correct and is the right choice. Only the label is wrong.

## Impact

Marked 🔒, so it is on the reviewed-as-security-critical list, and the
fail-open/fail-closed distinction is the entire content of that review. A
reader trusting the label concludes the processor leaks on misconfiguration
(it does not), or — worse, in the other direction — carries the "fails open"
reading to `CouchDbUrlPolicy`, which genuinely does fail open and is the one
that needs verifying against a real span.

`docs/allowlist.md` should be checked for the same wording.

## Fix

Reword to state both directions once, plainly:

```
/// 🔒 Exact host match, failing CLOSED: a host missing from configuration is
/// not treated as CouchDB, so url.full is denied rather than allowed. Such a
/// host loses diagnostic value and never leaks. Note this is the opposite
/// direction to CouchDbUrlPolicy's redaction, which fails OPEN on a host
/// mismatch — verify that one against a real span.
```

## Fixed (2026-09-07)

`AllowlistProcessor.IsCouchDbSpan` remarks now state the direction once and
correctly — fails **closed**, with the contrast to `CouchDbUrlPolicy` (which
fails **open**) as a separate paragraph naming that one as the path to verify
against a real span. Behaviour unchanged; only the label was wrong.

## Checked for the same wording elsewhere

`grep` for fail-open/fail-closed across `docs/` and `src/` returns seven other
hits. All seven are about `CouchDbUrlPolicy`'s redaction, which genuinely does
fail open, and all seven are correct:

- `RaksawiObservabilityOptions.cs:58`, `AttributeAllowlist.cs:159`
- `docs/adr/0023-couchdb-changes-the-database-surface.md:61`
- `docs/onboarding/integration.md:96`, `docs/onboarding/infra.md:40,76`
- `docs/demo/integration.md:56`, `docs/demo/plan.md:78`

`AllowlistProcessor.cs` was the only wrong one.

## Verified

`check.sh` clean, `test-fast.sh` 140/140.

## Comments
