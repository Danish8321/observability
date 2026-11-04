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
# quarter of an hour. Runs as its own compose project, removed on exit; it
# needs the local stack's ports free.
set -eu

cd "$(dirname "$0")/../.."

PROJECT=raksawi-alert-check
COMPOSE="docker compose -f deploy/docker-compose.yaml -p $PROJECT"
RULES=deploy/grafana/provisioning/alerting/rules.yaml
MAILPIT=http://localhost:8025/api/v1
MIMIR_QUERY=http://localhost:9009/prometheus/api/v1/query
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

# await TITLE RUNBOOK — wait for a firing email whose subject names the alert,
# then check it links that alert's own runbook (plan M9).
await() {
    deadline=$(( $(date +%s) + WAIT_SECONDS ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        if subjects | grep -F "$1" | grep -qF 'FIRING'; then
            pass "email: $1"
            linked "$1" "$2"
            return 0
        fi
        sleep 10
    done
    fail "no email for \"$1\" within ${WAIT_SECONDS}s. Mailpit holds:"
    subjects >&2
}

# linked TITLE RUNBOOK — the runbook exists, and TITLE's email links it.
linked() {
    if [ ! -f "docs/runbooks/$2" ]; then
        fail "docs/runbooks/$2 does not exist"
        return 0
    fi
    id="$(curl -sf -G "$MAILPIT/search" --data-urlencode "query=subject:\"$1\"" | grep -o '"ID":"[^"]*"' | head -1 | cut -d'"' -f4)"
    case "$(curl -sf "$MAILPIT/message/$id" || :)" in
        *"docs/runbooks/$2"*) pass "links $2" ;;
        *) fail "\"$1\" email does not link docs/runbooks/$2" ;;
    esac
}

# absent TITLE — the alert must not have emailed.
absent() {
    if subjects | grep -qF "$1"; then fail "unexpected email: $1"; else pass "no email: $1"; fi
}

# series WHAT QUERY EXPECT — a Mimir query a rule rests on returns EXPECT. A
# rule whose query matches nothing never fires and never says so.
series() {
    case "$(curl -sf -G "$MIMIR_QUERY" --data-urlencode "query=$2" || :)" in
        *"$3"*) pass "$1" ;;
        *) fail "$1: $2 returned no $3" ;;
    esac
}

echo "== phase A: Loki export broken =="
if ! LOKI_OTLP_ENDPOINT=http://loki-unreachable.invalid:3100/otlp \
        $COMPOSE up -d --wait --wait-timeout 300 >/dev/null 2>&1; then
    echo "e2e-alerting.sh: the stack did not come up." >&2
    $COMPOSE ps -a >&2 || true
    $COMPOSE logs --tail 20 grafana collector >&2 || true
    exit 1
fi

# Counted against rules.yaml, so a rule Grafana did not load fails here.
# Compose reports Grafana running before its HTTP server answers; retry.
expected="$(grep -c '^      - uid:' "$RULES")"
i=0
while :; do
    loaded="$(curl -sf -u admin:admin http://localhost:3000/api/v1/provisioning/alert-rules | grep -o '"uid":"[^"]*"' | wc -l | tr -d ' ')"
    [ "$loaded" -eq "$expected" ] && break
    i=$((i + 1)); [ "$i" -gt 30 ] && break
    sleep 2
done
if [ "$loaded" -eq "$expected" ]; then
    pass "all $expected alert rules provisioned"
else
    fail "$loaded of $expected alert rules provisioned"
fi

# The queue only fills if something is waiting to go out.
(
    while :; do
        now="$(date +%s)000000000"
        curl -s -o /dev/null -X POST http://localhost:4319/v1/logs -H 'Content-Type: application/json' -d '{"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"alert-check"}}]},"scopeLogs":[{"logRecords":[{"timeUnixNano":"'"$now"'","body":{"stringValue":"alert check"}}]}]}]}' || :
        sleep 5
    done
) &
sender=$!

await "Alerting heartbeat" alerting-heartbeat.md
await "Export stalled" collector-export-stalled.md

# No fault below proves "Collector queue filling" fires, or that the sent-rate
# half of "Export stalled" matches anything, so check their series exist. The
# expressions are the rules' own (rules.yaml); change both together.
series "environment label on collector series" \
    'up{job="otelcol",deployment_environment_name="local"}' '"deployment_environment_name":"local"'
series "queue ratio matches (Collector queue filling)" \
    'otelcol_exporter_queue_size / otelcol_exporter_queue_capacity' '"exporter":"otlphttp/mimir"'
series "sent rate matches (Export stalled)" \
    'sum by (deployment_environment_name, exporter) (rate({__name__=~"otelcol_exporter_sent_(spans|metric_points|log_records)_total"}[5m])) > 0' \
    '"exporter":"otlphttp/mimir"'

# A wrongly firing alert started with the stack too and needs as long as
# "Collector not reporting" (~6.5 min) to email; give it that before saying
# none came.
sleep 120
absent "Collector not reporting"
absent "Store unreachable"
kill "$sender" 2>/dev/null || true
sender=""

echo "== phase B: collector and Loki stopped =="
curl -sf -X DELETE "$MAILPIT/messages" >/dev/null
$COMPOSE stop collector loki >/dev/null 2>&1

await "Collector not reporting" collector-not-reporting.md
await "Store unreachable: Loki" store-unreachable.md
absent "Store unreachable: Mimir"

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "e2e-alerting.sh: FAILED." >&2
    exit 1
fi

echo "OK"
