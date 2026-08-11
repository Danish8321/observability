#!/usr/bin/env sh
# Proves redaction and allowlisting by inspecting stored telemetry, not
# configuration — Rev 3 Gate 3 requires this because a test that reads
# configuration verifies intent, not outcome.
#
# Blocked, not implemented: needs a live collector + backing store to send a
# real span through and query back. The demo stack (deploy/docker-compose.yaml)
# is dummy-data-only and has no allowlist enforcement yet (ADR-0022), so even
# a working e2e.sh against it today could only prove CouchDB URL redaction
# (CouchDbUrlPolicy) — not allowlist behaviour, since nothing enforces one.
#
# A real e2e.sh needs, at minimum:
#   1. deploy/docker-compose.yaml stack up (nats, couchdb, collector) plus a
#      queryable backend (SigNoz, started separately per samples/README.md)
#   2. a script or reference request that emits a span touching a CouchDB URL
#      (samples/Screening.Api gives one: app-1001 through its POST /applications
#      flow)
#   3. a query against the backend's API asserting url.full is redacted on
#      that span, and (once contract.sh's prerequisites exist) that no
#      non-allowlisted key reached storage
#
# This script fails loudly instead of faking a pass, per the project's
# evidence rule. Replace this file once the allowlist enforcement point in
# contract.sh's blockers exists — until then this can prove redaction only,
# which is a narrower claim than what e2e.sh is meant to certify.
set -eu

echo "e2e.sh: no reachable collector/backend passed to this script, and no" >&2
echo "allowlist enforcement exists to assert against even if there were one" >&2
echo "(see comment at the top of this script). Not a pass." >&2
exit 1
