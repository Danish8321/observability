#!/usr/bin/env sh
# Proves the platform alerts reach a person (plan D3): in the local stack,
# breaking the collector's export, stopping the collector and stopping a store
# each delivers its own email to Mailpit, and the daily heartbeat arrives.
#
# Two phases, because a stopped collector cannot also show a stalled export:
#   A. Loki export pointed at a host that does not exist, logs sent throughout:
#      "Export stalled" and the heartbeat arrive; nothing else does.
#   B. collector and Loki both stopped: "Collector not reporting" and
#      "Store unreachable: Loki" arrive. The two are independent — one is read
#      from Mimir, the other is Grafana's own failed query.
#
# Each alert needs minutes of "for" before it emails, so this takes about a
# quarter of an hour. Runs as its own compose project, removed on exit.
set -eu

cd "$(dirname "$0")/../.."

PROJECT=raksawi-alert-check
COMPOSE="docker compose -f deploy/docker-compose.yaml -p $PROJECT"
MAILPIT=http://localhost:8025/api/v1
# Long enough for the slowest rule (2m absence + 3m for + 1m evaluation +
# 30s group wait) with room for a slow first evaluation.
WAIT_SECONDS=720

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    echo "e2e-alerting.sh: no running docker daemon. This script asserts on" >&2
    echo "emails Grafana actually sent and cannot be faked without one." >&2
    echo "Not a pass." >&2
    exit 1
fi

sender=""
cleanup() {
    [ -n "$sender" ] && kill "$sender" 2>/dev/null || true
    if [ "${E2E_KEEP:-0}" = "1" ]; then
        echo "E2E_KEEP=1: project $PROJECT left running." >&2
        return 0
    fi
    $COMPOSE down -v >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker network create observability >/dev/null 2>&1 || true

failed=0
pass() { echo "  ok      $1"; }
fail() { echo "  FAILED  $1" >&2; failed=1; }

# Subjects of every email Mailpit holds, one per line.
subjects() {
    curl -sf "$MAILPIT/messages?limit=200" | grep -o '"Subject":"[^"]*"' || :
}

# await TITLE — wait for a firing email whose subject names the alert.
await() {
    deadline=$(( $(date +%s) + WAIT_SECONDS ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        if subjects | grep -F "$1" | grep -qF 'FIRING'; then
            pass "email: $1"
            return 0
        fi
        sleep 10
    done
    fail "no email for \"$1\" within ${WAIT_SECONDS}s. Mailpit holds:"
    subjects >&2
    return 0
}

# Every email's body must link a runbook in docs/runbooks/.
check_runbook_links() {
    for id in $(curl -sf "$MAILPIT/messages?limit=200" | grep -o '"ID":"[^"]*"' | cut -d'"' -f4); do
        body="$(curl -sf "$MAILPIT/message/$id" || :)"
        subject="$(printf '%s' "$body" | grep -o '"Subject":"[^"]*"' | head -1)"
        case "$body" in
            *docs/runbooks/*.md*) pass "runbook linked in $subject" ;;
            *) fail "no runbook link in $subject" ;;
        esac
    done
}

# absent TITLE — the alert must not have emailed.
absent() {
    if subjects | grep -qF "$1"; then fail "unexpected email: $1"; else pass "no email: $1"; fi
}

echo "== phase A: Loki export broken =="
if ! LOKI_OTLP_ENDPOINT=http://loki-unreachable.invalid:3100/otlp \
        $COMPOSE up -d --wait --wait-timeout 300 >/dev/null 2>&1; then
    echo "e2e-alerting.sh: the stack did not come up." >&2
    $COMPOSE ps -a >&2 || true
    $COMPOSE logs --tail 20 grafana collector >&2 || true
    exit 1
fi

# A provisioning file Grafana rejects in part still lets it start, so count.
# Compose reports Grafana running before its HTTP server answers; retry.
i=0
while :; do
    rules="$(curl -sf -u admin:admin http://localhost:3000/api/v1/provisioning/alert-rules | grep -o '"uid":"[^"]*"' | wc -l)"
    [ "$rules" -ge 6 ] && break
    i=$((i + 1)); [ "$i" -gt 30 ] && break
    sleep 2
done
if [ "$rules" -ge 6 ]; then pass "$rules alert rules provisioned"; else fail "only $rules alert rules provisioned"; fi

# The queue only fills if something is waiting to go out.
(
    while :; do
        now="$(date +%s)000000000"
        curl -s -o /dev/null -X POST http://localhost:4319/v1/logs -H 'Content-Type: application/json' -d '{"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"alert-check"}}]},"scopeLogs":[{"logRecords":[{"timeUnixNano":"'"$now"'","body":{"stringValue":"alert check"}}]}]}]}' || :
        sleep 5
    done
) &
sender=$!

await "Alerting heartbeat"
await "Export stalled"
absent "Collector not reporting"
absent "Store unreachable"
check_runbook_links
kill "$sender" 2>/dev/null || true
sender=""

echo "== phase B: collector and Loki stopped =="
curl -sf -X DELETE "$MAILPIT/messages" >/dev/null
$COMPOSE stop collector loki >/dev/null 2>&1

await "Collector not reporting"
await "Store unreachable: Loki"
absent "Store unreachable: Mimir"
check_runbook_links

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "e2e-alerting.sh: FAILED." >&2
    exit 1
fi

echo "OK"
