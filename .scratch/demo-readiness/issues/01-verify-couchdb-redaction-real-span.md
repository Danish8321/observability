Status: open

# Verify CouchDB URL redaction against a real span

`CouchDbUrlPolicy` (ADR-0023) redacts document IDs from `url.full` on CouchDB
HTTP spans by exact host match, and **fails open**: a wrong or missing host
value means no redaction and no error. `samples/README.md` explicitly says
this must be verified on a real span before it's trusted — no automated test
proves it today (`e2e.sh` is a stub blocked on the same gap, see
`.claude/scripts/e2e.sh`).

## What to do

1. Bring up the demo stack: `couchdb`, `nats`, a collector configured with a
   `file` exporter (or SigNoz) so exported spans can be inspected directly.
2. Run `samples/Screening.Api` pointed at that stack.
3. `POST /applications` with a body that would produce a CouchDB document ID
   recognizable in a URL (e.g. the `applicationId`).
4. Inspect the exported span's `url.full` attribute. Confirm the document ID
   is redacted and the host match fired.
5. Also test the fail-open path deliberately: misconfigure `CouchDbHosts` (or
   leave it unset) and confirm the ID reaches the span unredacted with no
   error raised — this is documented behavior, not a bug, but should be
   observed once rather than assumed.

## Why this stalled (2026-08-11)

Attempted this session. Got as far as: collector (file exporter), CouchDB,
NATS all running in Docker; `Screening.Api` running locally on port 5099.
Blocked at the last step — POSTing a request to the API — because this
session's Bash permission classifier denies `curl`/`wget` entirely (network
request calls blocked outright, not just flagged). No workaround found within
the session; needs either a permission grant for outbound HTTP in Bash, or
the POST run by a human and the result relayed back.

Notes for next attempt:
- Docker Desktop on Windows: bind-mount source paths must live under
  `C:\Users\...`, not git-bash's `/tmp` (which resolves outside the WSL2 VM
  Docker Desktop shares with Windows) — mount failed silently as an empty
  directory otherwise.
- `otelcol` container images are distroless — no shell, can't `mkdir` inside
  them at runtime. Either mount a pre-existing host directory for
  `file_storage`, or don't use `file_storage` at all for scratch verification
  (a minimal `file` exporter config skips this entirely).
- Local `dotnet run` for `Screening.Api` collided with a stray process
  already bound to port 5000 — set `ASPNETCORE_URLS` explicitly to avoid it.

## Comments
