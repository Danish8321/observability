Status: closed

# `url.full` leaks CouchDB credentials in cleartext — `CouchDbUrlPolicy.Redact` strips the document ID but not userinfo

Confirmed live 2026-08-11, querying real exported spans in ClickHouse after
issue 04 was fixed:

```
url.full = http://admin:password@localhost:5984/kyc/{docid}
```

The document ID is correctly redacted (`{docid}`) — that part of
`CouchDbUrlPolicy` works as documented. The credentials are not.

## Root cause

`src/Raksawi.Observability/CouchDbUrlPolicy.cs`, `Redact()`:

```csharp
builder.Append(uri.GetLeftPart(UriPartial.Authority));
```

`Uri.GetLeftPart(UriPartial.Authority)` includes userinfo when present —
`scheme://user:password@host:port`. `samples/Screening.Api/Program.cs:11` and
`Screening.Worker/Program.cs:9` both build the CouchDB client from
`"http://admin:password@localhost:5984"`, so every CouchDB span's `url.full`
carries the password in cleartext, unredacted, into the collector and store.

## Impact

This is a **secrets** exposure, not a Class-2-identifier exposure — a
different and more severe category than the one QD2 already closed.
`samples/README.md` and `data-protection-brief.md` both state as a design
invariant that "tokens, passwords, credentials, authorisation headers,
connection strings" never appear in telemetry. This violates that invariant
on every single CouchDB call, in a codebase whose entire compliance argument
rests on default-deny redaction actually working.

`CouchDbUrlPolicy`'s own doc comment says it "fails open" for wrong host
config — this is a different failure: right host, url.full attribute
correctly targeted and redacted, but only the path component. The authority
component was never in scope of the redaction logic at all.

## Fix

Two independent things need to change, and probably both:

1. `CouchDbUrlPolicy.Redact()` should strip userinfo from the authority
   unconditionally — `uri.GetLeftPart(UriPartial.Authority)` should never be
   used as-is; reconstruct scheme+host+port without credentials.
2. The demo's CouchDB client construction (`Program.cs` in both services)
   embeds credentials in the connection URL at all, which is what puts them on
   `HttpClient`'s `RequestUri` in the first place — using `Authorization:
   Basic` header-based auth instead of userinfo-in-URL would remove the source,
   independent of the redaction fix. (ADR-0022 already flags "credentials in
   the compose file" as demo scaffolding not production-shaped — this is the
   same category, but it also breaks the telemetry invariant, not just
   production-readiness.)

Fix (1) regardless of (2), since userinfo-in-URL is valid HTTP and the policy
should not assume callers never do it.

## Fixed (2026-08-11)

Applied fix (1). `CouchDbUrlPolicy.Redact()` no longer uses
`uri.GetLeftPart(UriPartial.Authority)` (userinfo included). Rebuilds the
authority explicitly from `uri.Scheme` + `"://"` + `uri.Authority` —
`Uri.Authority` excludes userinfo by definition, unlike `GetLeftPart`. Fix (2)
not applied — left as demo scaffolding per ADR-0022, tracked separately, not
a telemetry-invariant issue once (1) holds regardless of how the URL was
constructed.

Verified: `check.sh` clean, `test-fast.sh` 35/35. Live: fresh request
(`app-4001`) queried back from ClickHouse —
`url.full = http://localhost:5984/kyc/{docid}` on every CouchDB span, both
services. No credentials present.

## Comments
