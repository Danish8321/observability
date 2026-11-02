#!/usr/bin/env sh
# Proves the local stack works as a whole: deploy/docker-compose.yaml comes up
# from nothing, a synthetic OTLP payload sent to the collector is queryable in
# Loki and Mimir, and Grafana reports both data sources healthy (plan D2).
#
# e2e.sh and e2e-instrumented.sh stop at a file sink, so they prove what the
# collector lets through, not that a store accepts and returns it. This one
# reads back from the stores — and checks the allowlist held there too.
#
# Runs as its own compose project with its own volumes, removed on exit, so a
# local stack you are using keeps its data. It does need that stack's ports
# free. Traces are not covered: the trace store is not chosen yet.
set -eu

cd "$(dirname "$0")/../.."

PROJECT=raksawi-stack-check
COMPOSE="docker compose -f deploy/docker-compose.yaml -p $PROJECT"
RUN="stack-check-$(date +%s)"
CANARY="PII-CANARY-$RUN"

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    echo "e2e-stack.sh: no running docker daemon. This script asserts against" >&2
    echo "data read back from the stores and cannot be faked without one." >&2
    echo "Not a pass." >&2
    exit 1
fi

cleanup() {
    if [ "${E2E_KEEP:-0}" = "1" ]; then
        echo "E2E_KEEP=1: project $PROJECT left running." >&2
        return 0
    fi
    $COMPOSE down -v >/dev/null 2>&1 || true
}
trap cleanup EXIT

# The compose file attaches to this network so SigNoz can join it.
docker network create observability >/dev/null 2>&1 || true

echo "== bringing up the local stack =="
if ! $COMPOSE up -d --wait --wait-timeout 300 >/dev/null 2>&1; then
    echo "e2e-stack.sh: the stack did not come up." >&2
    $COMPOSE ps -a >&2 || true
    $COMPOSE logs --tail 20 >&2 || true
    exit 1
fi

now="$(date +%s)000000000"
resource='{"attributes":[{"key":"service.name","value":{"stringValue":"stack-check"}}]}'

# The collector's receiver is up once compose reports it running, but its
# pipelines may not be; retry the first send rather than sleeping.
send() {
    i=0
    until curl -sf -X POST "http://localhost:4319/v1/$1" \
            -H 'Content-Type: application/json' -d "$2" >/dev/null; do
        i=$((i + 1))
        if [ "$i" -gt 30 ]; then
            echo "e2e-stack.sh: the collector never accepted /v1/$1." >&2
            $COMPOSE logs --tail 30 collector >&2 || true
            exit 1
        fi
        sleep 2
    done
}

echo "== sending synthetic telemetry =="
send logs '{"resourceLogs":[{"resource":'"$resource"',"scopeLogs":[{"logRecords":[{
  "timeUnixNano":"'"$now"'","severityText":"Information",
  "body":{"stringValue":"stack check '"$RUN"'"},
  "attributes":[
    {"key":"correlation.id","value":{"stringValue":"'"$RUN"'"}},
    {"key":"applicantIdentifier","value":{"stringValue":"'"$CANARY"'"}}]}]}]}]}'

send metrics '{"resourceMetrics":[{"resource":'"$resource"',"scopeMetrics":[{"metrics":[{
  "name":"stack.check.probe",
  "gauge":{"dataPoints":[{"timeUnixNano":"'"$now"'","asDouble":1,
    "attributes":[{"key":"run","value":{"stringValue":"'"$RUN"'"}}]}]}}]}]}]}'

# poll NAME URL QUERY EXPECT — retry a store query until EXPECT appears.
poll() {
    i=0
    while :; do
        body="$(curl -sf -G "$2" --data-urlencode "$3" 2>/dev/null || :)"
        case "$body" in *"$4"*) printf '%s' "$body"; return 0 ;; esac
        i=$((i + 1))
        if [ "$i" -gt 45 ]; then
            echo "e2e-stack.sh: $1 never returned the probe. Last response:" >&2
            echo "$body" >&2
            return 1
        fi
        sleep 2
    done
}

failed=0

echo "== reading back from the stores =="
start="$(( $(date +%s) - 600 ))000000000"
if loki="$(poll Loki "http://localhost:3100/loki/api/v1/query_range?start=$start" \
        "query={service_name=\"stack-check\"} |= \"$RUN\"" "stack check $RUN")"; then
    echo "  ok      Loki returned the log record"

    # The allowed key coming back shows structured metadata is in the
    # response, which is what makes the absence below mean something.
    case "$loki" in
        *correlation_id*) echo "  ok      kept    correlation.id in Loki" ;;
        *) echo "  FAILED  missing correlation.id in Loki" >&2; failed=1 ;;
    esac
    case "$loki" in
        *"$CANARY"*) echo "  FAILED  LEAKED  applicantIdentifier reached Loki" >&2; failed=1 ;;
        *) echo "  ok      dropped applicantIdentifier before Loki" ;;
    esac
else
    failed=1
fi

if poll Mimir "http://localhost:9009/prometheus/api/v1/query" \
        "query=stack_check_probe{run=\"$RUN\"}" "\"run\":\"$RUN\"" >/dev/null; then
    echo "  ok      Mimir returned the metric"
else
    failed=1
fi

echo "== Grafana data sources =="
for uid in mimir loki; do
    health="$(curl -s -u admin:admin "http://localhost:3000/api/datasources/uid/$uid/health" || :)"
    case "$health" in
        *'"status":"OK"'*) echo "  ok      $uid healthy" ;;
        *) echo "  FAILED  $uid: $health" >&2; failed=1 ;;
    esac
done

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "e2e-stack.sh: FAILED." >&2
    exit 1
fi

echo "OK"
