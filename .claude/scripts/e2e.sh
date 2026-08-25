#!/usr/bin/env sh
# Proves redaction and allowlisting by inspecting telemetry that came out of
# the collector, not by reading configuration — Rev 3 Gate 3 requires this
# because a test that reads configuration verifies intent, not outcome.
#
# Shape: two collectors. The one under test runs deploy/collector/config.yaml
# BYTE-FOR-BYTE UNMODIFIED. The sink is named signoz-ingester-1, which is the
# endpoint that config already exports to, and writes what it receives to a
# file. So what is asserted on is what survived the policy. A test that edits
# the thing it is testing proves nothing, which is why the config is not
# templated, patched, or overlaid here.
#
# Scope, stated honestly: this exercises the COLLECTOR enforcement point
# (ADR-0009) — the only one that reaches agent-instrumented services. The
# in-process allowlist is covered by unit tests, and the analyzer by the
# compilation tests. A full-stack run through Screening.Api and SigNoz would
# also exercise CouchDbUrlPolicy; that is a larger fixture and is not this.
set -eu

cd "$(dirname "$0")/../.."

IMAGE=otel/opentelemetry-collector-contrib:0.140.1
NET=raksawi-e2e
SINK=signoz-ingester-1
SUT=raksawi-e2e-collector
PORT=14318

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    echo "e2e.sh: no running docker daemon. This script asserts against" >&2
    echo "received telemetry and cannot be faked without one. Not a pass." >&2
    exit 1
fi

out_dir="$(mktemp -d)"

# Both collectors write files, and neither can write into a bind-mounted host
# directory: they run as uid 10001 and this is a Windows host. So both writable
# paths are docker named volumes, and the received telemetry is copied back out
# with a throwaway container.
#
# QUEUE_VOL backs the shipped config's file_storage sending queue, without
# which that collector refuses to start. OUT_VOL is where the sink writes what
# it received.
QUEUE_VOL=raksawi-e2e-queue
OUT_VOL=raksawi-e2e-out
docker volume create "$QUEUE_VOL" >/dev/null
docker volume create "$OUT_VOL" >/dev/null
# A fresh docker volume is root-owned. The collector image is distroless and
# has no shell or chown, hence busybox.
MSYS_NO_PATHCONV=1 docker run --rm --user 0 \
    -v "$QUEUE_VOL":/storage -v "$OUT_VOL":/out busybox:1.36 \
    chown -R 10001:10001 /storage /out

received="$out_dir/received.json"

# Copy the sink's output back to the host. Empty (not an error) until the sink
# has written something.
dump_received() {
    MSYS_NO_PATHCONV=1 docker run --rm -v "$OUT_VOL":/out busybox:1.36 \
        sh -c 'cat /out/received.json 2>/dev/null' >"$received" 2>/dev/null || :
}

cleanup() {
    docker rm -f "$SUT" "$SINK" >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
    docker volume rm "$QUEUE_VOL" "$OUT_VOL" >/dev/null 2>&1 || true
    rm -rf "$out_dir"
}
trap cleanup EXIT

echo "== bringing up sink and collector under test =="
docker network create "$NET" >/dev/null 2>&1 || true

MSYS_NO_PATHCONV=1 docker run -d --name "$SINK" --network "$NET" \
    -v "$(pwd)/.claude/scripts/e2e/sink-config.yaml":/etc/otelcol/config.yaml:ro \
    -v "$OUT_VOL":/out \
    "$IMAGE" --config=/etc/otelcol/config.yaml >/dev/null

MSYS_NO_PATHCONV=1 docker run -d --name "$SUT" --network "$NET" \
    -v "$(pwd)/deploy/collector":/etc/otelcol:ro \
    -v "$QUEUE_VOL":/var/lib/otelcol/storage \
    -p "$PORT":4318 \
    "$IMAGE" --config=/etc/otelcol/config.yaml >/dev/null

# The collector under test has no health endpoint enabled, so wait on the
# receiver actually answering rather than on a fixed sleep.
i=0
until curl -s -o /dev/null -X POST "http://localhost:$PORT/v1/traces" \
        -H 'Content-Type: application/json' -d '{}' 2>/dev/null; do
    i=$((i + 1))
    if [ "$i" -gt 60 ]; then
        echo "e2e.sh: collector under test never accepted a request." >&2
        docker logs "$SUT" >&2 || true
        exit 1
    fi
    sleep 1
done

echo "== sending probe telemetry =="
curl -sf -X POST "http://localhost:$PORT/v1/traces" \
    -H 'Content-Type: application/json' \
    --data-binary @.claude/scripts/e2e/payload-traces.json >/dev/null
curl -sf -X POST "http://localhost:$PORT/v1/metrics" \
    -H 'Content-Type: application/json' \
    --data-binary @.claude/scripts/e2e/payload-metrics.json >/dev/null

# batch has a 5s timeout; wait for the sink to write rather than guessing.
i=0
dump_received
while [ ! -s "$received" ]; do
    i=$((i + 1))
    if [ "$i" -gt 60 ]; then
        echo "e2e.sh: nothing reached the sink within 60s." >&2
        docker logs "$SUT" >&2 || true
        docker logs "$SINK" >&2 || true
        exit 1
    fi
    sleep 2
    dump_received
done
# Traces and metrics batch independently; give the second one time to land.
sleep 8
dump_received

failed=0

# present KEY — the key must have survived the collector.
present() {
    if grep -q "\"$1\"" "$received"; then
        echo "  ok      kept    $1"
    else
        echo "  FAILED  missing $1 — allowlisted telemetry was dropped" >&2
        failed=1
    fi
}

# absent KEY — the key must NOT be in what reached the sink.
absent() {
    if grep -q "\"$1\"" "$received"; then
        echo "  FAILED  LEAKED  $1 — reached the store" >&2
        failed=1
    else
        echo "  ok      dropped $1"
    fi
}

echo "== asserting against received telemetry =="

# Allowed by family, and a declared Class 2 key.
present http.request.method
present server.address
present correlation.id

# 🔒 Carve-outs. Each of these is in an allowed family and must still be gone.
absent http.request.header.authorization
absent db.statement

# Not in any family and declared by nobody. The shape a hand-rolled PII
# attribute actually takes.
absent applicantIdentifier

# 🔒 Metric dimensions: Class 2 is permitted on the span above and refused
# here. Same key, opposite verdict, which is the rule stated as a test.
if grep -q '"reason"' "$received"; then
    echo "  ok      kept    reason (bounded metric dimension)"
else
    echo "  FAILED  missing reason — bounded dimension was dropped" >&2
    failed=1
fi

# The conditional pair. url.full survives only on the CouchDB span; the
# ordinary span's copy must be gone. Both spans are in the same payload, so
# the assertion is on which URL survived, not on whether the key exists.
if grep -q 'api.example.test/applications/12345' "$received"; then
    echo "  FAILED  LEAKED  url.full on an ordinary span" >&2
    failed=1
else
    echo "  ok      dropped url.full on an ordinary span"
fi

if grep -q 'couchdb/applications' "$received"; then
    echo "  ok      kept    url.full on a CouchDB span"
else
    echo "  FAILED  missing url.full on a CouchDB span — the redacted URL is" >&2
    echo "          the whole diagnostic value of that span" >&2
    failed=1
fi

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "e2e.sh: FAILED. Received telemetry is above the assertions." >&2
    exit 1
fi

echo "OK"
