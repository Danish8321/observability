#!/usr/bin/env sh
# Proves the demo end to end in the local stack (plan D4): an application whose
# screening fails is abandoned, its trace opens in Tempo through Grafana, the
# runbook's own queries find it, and the "Work abandoned" email reaches Mailpit
# linking that runbook; and the collector's tail sampling keeps every abandoned
# trace while dropping most healthy ones. Each later D4 section adds its checks
# here.
#
# The trace ID is chosen here and sent as a W3C traceparent. The services
# sample parent-based, so the whole flow, NATS hop included, joins that trace
# and the checks know what to look for.
#
# The alert waits out its 5m "for" before it emails, so this takes about ten
# minutes. Runs as its own compose project, removed on exit, and builds the
# services from the current source; it needs the local stack's ports free.
set -eu

cd "$(dirname "$0")/../.."

PROJECT=raksawi-demo-check
COMPOSE="docker compose -f deploy/docker-compose.yaml -p $PROJECT"
API=http://localhost:5206
GRAFANA=http://localhost:3000
MAILPIT=http://localhost:8025/api/v1
MIMIR_QUERY=http://localhost:9009/prometheus/api/v1/query
RUNBOOK=docs/runbooks/work-abandoned.md
# 1m metric export + 1m evaluation + 5m "for" + 30s group wait, with room.
WAIT_SECONDS=900

# Starts nonzero: Tempo's search results drop a trace ID's leading zeros.
new_trace_id() { echo "e2e0$(od -An -N14 -tx1 /dev/urandom | tr -d ' \n')"; }
TRACE_ID="$(new_trace_id)"
SPAN_ID="$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
# "fail" in the ID is the demo fault (ScreeningService): the provider throws on
# every attempt, so the worker abandons the application.
APP_ID="app-fail-$(date +%s)"

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    echo "e2e-demo.sh: no running docker daemon. This script asserts on what" >&2
    echo "the running stack stored and emailed, and cannot be faked without" >&2
    echo "one. Not a pass." >&2
    exit 1
fi

log="$(mktemp)"
cleanup() {
    rm -f "$log"
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

# eventually SECONDS EXPECT CMD... — rerun CMD until its output contains
# EXPECT. The last output is left in $body either way.
eventually() {
    deadline=$(( $(date +%s) + $1 )); expect="$2"; shift 2
    while :; do
        body="$("$@" 2>/dev/null || :)"
        case "$body" in *"$expect"*) return 0 ;; esac
        [ "$(date +%s)" -ge "$deadline" ] && return 1
        sleep 5
    done
}

# trace ID — a trace opened by ID through Grafana, as Explore opens one.
# Neither helper uses -f, so a failed check prints the error body.
trace() {
    curl -s -u admin:admin -X POST "$GRAFANA/api/ds/query" \
        -H 'Content-Type: application/json' \
        -d '{"from":"now-1h","to":"now","queries":[{"refId":"A","datasource":{"uid":"tempo"},"queryType":"traceId","query":"'"$1"'"}]}'
}

# search TRACEQL — a TraceQL search as Explore runs one: in the browser,
# through Grafana's data source proxy. Grafana's query API refuses TraceQL
# searches ("backend TraceQL search queries are not supported").
search() {
    now="$(date +%s)"
    curl -s -G -u admin:admin "$GRAFANA/api/datasources/proxy/uid/tempo/api/search" \
        --data-urlencode "q=$1" --data-urlencode "start=$((now - 3600))" --data-urlencode "end=$now"
}

mimir() {
    curl -sf -G "$MIMIR_QUERY" --data-urlencode "query=$1"
}

# runbook_query LANG — the runbook's first LANG block, so what is checked is
# what a responder runs.
runbook_query() {
    awk -v lang="$1" '$0 == "```" lang { getline; print; exit }' "$RUNBOOK"
}

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

echo "== bringing up the local stack, services built from source =="
if ! $COMPOSE up -d --build --wait --wait-timeout 300 >"$log" 2>&1; then
    echo "e2e-demo.sh: the stack did not come up." >&2
    tail -40 "$log" >&2
    $COMPOSE ps -a >&2 || true
    exit 1
fi

# Core NATS keeps nothing for a subscriber that is not there yet, so a message
# published before the worker subscribes is gone.
if ! eventually 60 '"status":"ok"' curl -sf "$API/health" \
        || ! eventually 60 'kyc.applications.submitted' curl -sf 'http://localhost:8222/connz?subs=1'; then
    echo "e2e-demo.sh: screening-api or screening-worker never became ready." >&2
    $COMPOSE logs --tail 30 screening-api screening-worker >&2 || true
    exit 1
fi

# submit APP_ID TRACE_ID — prints the HTTP status.
submit() {
    curl -s -o /dev/null -w '%{http_code}' -X POST "$API/applications" \
        -H 'Content-Type: application/json' \
        -H "traceparent: 00-$2-$SPAN_ID-01" \
        -d "{\"applicationId\":\"$1\",\"applicant\":\"Demo Applicant\"}" || :
}

echo "== submitting $APP_ID in trace $TRACE_ID =="
code="$(submit "$APP_ID" "$TRACE_ID")"
if [ "$code" = "202" ]; then
    pass "submitted: 202"
else
    echo "e2e-demo.sh: POST /applications returned $code, not 202." >&2
    $COMPOSE logs --tail 30 screening-api >&2 || true
    exit 1
fi

echo "== the trace, in Tempo through Grafana =="
if eventually 180 'screen application' trace "$TRACE_ID"; then
    for service in screening-api screening-worker; do
        case "$body" in
            *"\"$service\""*) pass "trace has $service spans" ;;
            *) fail "trace $TRACE_ID has no $service spans" ;;
        esac
    done
else
    fail "trace $TRACE_ID never opened through Grafana. Last response: $body"
fi

if eventually 120 "$TRACE_ID" search "$(runbook_query traceql)"; then
    pass "the runbook's TraceQL finds the trace"
else
    fail "the runbook's TraceQL did not find $TRACE_ID. Last response: $body"
fi

echo "== tail sampling (ADR-0039) =="
# 20 healthy then 10 failing, each its own trace. The worker takes messages in
# order, so once the last failing trace is stored every healthy one before it
# has been decided.
healthy=""; failing=""
n=0
while [ "$n" -lt 20 ]; do
    n=$((n + 1)); id="$(new_trace_id)"; healthy="$healthy $id"
    submit "app-ok-$n-$(date +%s)" "$id" >/dev/null
done
n=0
while [ "$n" -lt 10 ]; do
    n=$((n + 1)); id="$(new_trace_id)"; failing="$failing $id"
    submit "app-fail-$n-$(date +%s)" "$id" >/dev/null
done

lost=0
for id in $failing; do
    eventually 180 'screen application' trace "$id" || lost=$((lost + 1))
done
if [ "$lost" -eq 0 ]; then
    pass "all 10 abandoned traces are stored"
else
    fail "$lost of 10 abandoned traces were not stored"
fi

kept=0
for id in $healthy; do
    case "$(trace "$id" 2>/dev/null || :)" in *'screen application'*) kept=$((kept + 1)) ;; esac
done
if [ "$kept" -lt 20 ]; then
    pass "healthy traces are sampled: $kept of 20 stored"
else
    fail "all 20 healthy traces were stored: nothing is being sampled out"
    echo "          decisions by policy: $(mimir 'sum by (policy, sampled) ({__name__=~"otelcol_processor_tail_sampling_count_traces_sampled.*"})' || :)" >&2
fi

# The policy reads this key, so it has to survive the allowlist before it.
case "$(trace "$TRACE_ID" 2>/dev/null || :)" in
    *'screening.abandoned'*) pass "the stored trace carries screening.abandoned" ;;
    *) fail "the stored trace has no screening.abandoned" ;;
esac

sampled='sum({__name__=~"otelcol_processor_tail_sampling_count_traces_sampled.*", policy="abandoned", sampled="true"}) > 0'
if eventually 180 '"value"' mimir "$sampled"; then
    pass "the collector reports the abandoned policy keeping traces"
else
    fail "no abandoned-policy decisions in Mimir: $body"
    echo "          tail sampling series in Mimir: $(mimir 'group by (__name__, policy, sampled) ({__name__=~"otelcol_processor_tail_sampling.*"})' || :)" >&2
fi

echo "== the alert =="
# The rule reads this series. Without it nothing can email, so the wait for
# the email is skipped rather than spent.
if eventually 180 '"reason":"ScreeningProviderException"' mimir "$(runbook_query promql)"; then
    pass "the runbook's first query shows the abandonment"
    await "Work abandoned" work-abandoned.md
else
    fail "the runbook's first query shows no ScreeningProviderException: $body"
    echo "          screening series in Mimir: $(mimir 'group by (__name__) ({__name__=~"screening.*"})' || :)" >&2
fi

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "e2e-demo.sh: FAILED." >&2
    exit 1
fi

echo "OK"
