#!/usr/bin/env sh
# Runs the reference services for real and asserts on the telemetry they
# produced — the gap e2e.sh leaves open and says so.
#
# e2e.sh posts a hand-written OTLP payload at the collector, so it proves the
# COLLECTOR enforcement point and nothing else. Everything the library does
# before export — the in-process allowlist, meter registration, CouchDB URL
# redaction — was covered only by unit tests, which prove the code does what
# the test says, not that a running service emits it.
#
# Shape: screening-api and screening-worker run as containers on a docker
# network, exporting to deploy/collector/config.yaml BYTE-FOR-BYTE UNMODIFIED,
# which forwards to a sink that writes what it received to a file. So the
# assertions below are on telemetry that survived both enforcement points
# after leaving a real process.
#
# Why containers rather than dotnet run on the host: the collector keeps
# url.full only when server.address is the CouchDB host, so a service reaching
# CouchDB on localhost would have url.full dropped by the collector before any
# assertion could see whether the library redacted it. The hostname is
# load-bearing, so the services need the network DNS.
set -eu

cd "$(dirname "$0")/../.."

COLLECTOR_IMAGE=otel/opentelemetry-collector-contrib:0.140.1
RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
NET=raksawi-e2e-svc
SINK=signoz-ingester-1
SUT=raksawi-e2e-svc-collector
COUCH=couchdb
NATSC=nats
# The DNS name the prometheus receiver's scrape target is written against.
NATSEXP=nats-exporter
API=raksawi-e2e-api
WORKER=raksawi-e2e-worker
COUCH_PORT=15984
API_PORT=18080

# 🔒 Class 3. Written to CouchDB, and must appear in no span, log or metric.
# The single most valuable assertion in this file: it is the failure that
# cannot be undone once it has reached a store.
CANARY="PII-CANARY-Anneliese-Vandenberg"
APP_ID="e2e-$(date +%s)"

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    echo "e2e-instrumented.sh: no running docker daemon. This script asserts" >&2
    echo "against telemetry emitted by real services and cannot be faked" >&2
    echo "without one. Not a pass." >&2
    exit 1
fi

QUEUE_VOL=raksawi-e2e-svc-queue
OUT_VOL=raksawi-e2e-svc-out
out_dir="$(mktemp -d)"
received="$out_dir/received.json"

purge() {
    docker rm -f "$API" "$WORKER" "$SUT" "$SINK" "$COUCH" "$NATSC" "$NATSEXP" >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
    docker volume rm "$QUEUE_VOL" "$OUT_VOL" >/dev/null 2>&1 || true
}

cleanup() {
    # E2E_KEEP=1 leaves everything up for inspection after a failure. It does
    # not affect the purge below: a previous run still running is exactly what
    # would make this one assert against stale telemetry.
    if [ "${E2E_KEEP:-0}" = "1" ]; then
        echo "E2E_KEEP=1: containers, network and volumes left running." >&2
        echo "The next run purges them." >&2
        return 0
    fi
    purge
    rm -rf "$out_dir"
}
# Anything left from an interrupted run would otherwise fail the creates below.
purge
trap cleanup EXIT

echo "== publishing the reference services =="
# bin/ is gitignored at any depth, so this output cannot be committed by
# accident.
dotnet publish samples/Screening.Api/Screening.Api.csproj \
    -c Release -o bin/e2e/api --nologo -v q >/dev/null
dotnet publish samples/Screening.Worker/Screening.Worker.csproj \
    -c Release -o bin/e2e/worker --nologo -v q >/dev/null

echo "== bringing up infrastructure =="
docker network create "$NET" >/dev/null 2>&1 || true
docker volume create "$QUEUE_VOL" >/dev/null
docker volume create "$OUT_VOL" >/dev/null
# Fresh volumes are root-owned; the collector image is distroless and has no
# shell to chown with.
MSYS_NO_PATHCONV=1 docker run --rm --user 0 \
    -v "$QUEUE_VOL":/storage -v "$OUT_VOL":/out busybox:1.36 \
    chown -R 10001:10001 /storage /out

# The container name is the DNS name, and "couchdb" is exactly what the
# collector url.full rule matches on. Renaming this container silently changes
# what this test proves.
docker run -d --name "$COUCH" --network "$NET" \
    -e COUCHDB_USER=admin -e COUCHDB_PASSWORD=password \
    -p "$COUCH_PORT":5984 couchdb:3.5 >/dev/null

docker run -d --name "$NATSC" --network "$NET" \
    nats:2.12-alpine -js -m 8222 >/dev/null

# The broker runs none of our code and cannot be instrumented, so its health
# reaches the pipeline by scrape or not at all (ADR-0030). Started here rather
# than assumed, because the collector config under test names it as a target:
# without it the scrape fails and the pipeline is silently short one source.
docker run -d --name "$NATSEXP" --network "$NET" \
    natsio/prometheus-nats-exporter:0.17.3 \
    -varz -connz -subz -jsz=all "http://$NATSC:8222" >/dev/null

MSYS_NO_PATHCONV=1 docker run -d --name "$SINK" --network "$NET" \
    -v "$(pwd)/.claude/scripts/e2e/sink-config.yaml":/etc/otelcol/config.yaml:ro \
    -v "$OUT_VOL":/out \
    "$COLLECTOR_IMAGE" --config=/etc/otelcol/config.yaml >/dev/null

# The sink is waited on before the collector under test starts. A collector
# whose export target is not listening yet queues and retries with backoff,
# which turns a startup race into a failure unrelated to what is asserted.
wait_ready() {
    j=0
    until docker logs "$1" 2>&1 | grep -q "Everything is ready"; do
        j=$((j + 1))
        if [ "$j" -gt 60 ]; then
            echo "e2e-instrumented.sh: $1 never became ready." >&2
            docker logs "$1" >&2 || true
            exit 1
        fi
        sleep 1
    done
}
wait_ready "$SINK"

MSYS_NO_PATHCONV=1 docker run -d --name "$SUT" --network "$NET" \
    -v "$(pwd)/deploy/collector":/etc/otelcol:ro \
    -v "$QUEUE_VOL":/var/lib/otelcol/storage \
    "$COLLECTOR_IMAGE" --config=/etc/otelcol/config.yaml >/dev/null

wait_ready "$SUT"

echo "== waiting for CouchDB =="
i=0
until curl -sf "http://admin:password@localhost:$COUCH_PORT/_up" >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -gt 90 ]; then
        echo "e2e-instrumented.sh: CouchDB never came up." >&2
        docker logs "$COUCH" >&2 || true
        exit 1
    fi
    sleep 1
done
# The services do not create the database; a missing one fails the first PUT.
curl -sf -X PUT "http://admin:password@localhost:$COUCH_PORT/kyc" >/dev/null 2>&1 || true

echo "== starting the instrumented services =="
# OTEL_METRIC_EXPORT_INTERVAL: the SDK default is 60s, which would make this
# script wait a minute to see a counter. Standard OTel environment variable,
# not a change to anything under test — same pipeline, faster clock.
SVC_ENV="-e Otlp__Endpoint=http://$SUT:4318 \
    -e CouchDb__Url=http://admin:password@$COUCH:5984 \
    -e Nats__Url=nats://$NATSC:4222 \
    -e OTEL_METRIC_EXPORT_INTERVAL=5000 \
    -e DOTNET_ENVIRONMENT=Production -e ASPNETCORE_ENVIRONMENT=Production"

# Worker first: the sample subscribes with core NATS, which has no persistence,
# so a message published before the subscription exists is simply gone.
# shellcheck disable=SC2086
MSYS_NO_PATHCONV=1 docker run -d --name "$WORKER" --network "$NET" \
    -v "$(pwd)/bin/e2e/worker":/app:ro $SVC_ENV \
    "$RUNTIME_IMAGE" dotnet /app/Screening.Worker.dll >/dev/null

# shellcheck disable=SC2086
MSYS_NO_PATHCONV=1 docker run -d --name "$API" --network "$NET" \
    -v "$(pwd)/bin/e2e/api":/app:ro $SVC_ENV \
    -e ASPNETCORE_URLS=http://0.0.0.0:8080 -p "$API_PORT":8080 \
    "$RUNTIME_IMAGE" dotnet /app/Screening.Api.dll >/dev/null

i=0
until curl -sf "http://localhost:$API_PORT/health" >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -gt 60 ]; then
        echo "e2e-instrumented.sh: screening-api never became healthy." >&2
        docker logs "$API" >&2 || true
        exit 1
    fi
    sleep 1
done
# The API is healthy before the worker subscription is necessarily active.
sleep 5

echo "== exercising the slice: POST /applications =="
curl -sf -X POST "http://localhost:$API_PORT/applications" \
    -H 'Content-Type: application/json' \
    -d "{\"applicationId\":\"$APP_ID\",\"applicant\":\"$CANARY\"}" >/dev/null

# The worker screens asynchronously; give it the message before waiting on
# export.
sleep 5

dump_received() {
    MSYS_NO_PATHCONV=1 docker run --rm -v "$OUT_VOL":/out busybox:1.36 \
        sh -c 'cat /out/received.json 2>/dev/null' >"$received" 2>/dev/null || :
}

echo "== waiting for telemetry to reach the sink =="
# Two batch delays (the library BatchExportProcessor, then the collector batch
# processor) plus the metric interval. Waits on content rather than a fixed
# sleep, then takes one more pass so a late metric batch is included.
i=0
dump_received
while ! grep -q 'screening.applications.screened' "$received" 2>/dev/null \
    || ! grep -q 'Screened {application.id}' "$received" 2>/dev/null \
    || ! grep -q 'gnatsd_varz_connections' "$received" 2>/dev/null \
    || ! grep -q 'otelcol_receiver_accepted_spans' "$received" 2>/dev/null; do
    i=$((i + 1))
    if [ "$i" -gt 120 ]; then
        echo "e2e-instrumented.sh: the worker metric, log or NATS scrape never arrived." >&2
        echo "--- nats-exporter ---" >&2; docker logs "$NATSEXP" >&2 || true
        echo "--- api ---" >&2; docker logs "$API" >&2 || true
        echo "--- worker ---" >&2; docker logs "$WORKER" >&2 || true
        echo "--- collector ---" >&2; docker logs "$SUT" >&2 || true
        echo "--- sink ---" >&2; docker logs "$SINK" >&2 || true
        echo "--- received so far ---" >&2; cat "$received" >&2 || true
        exit 1
    fi
    sleep 2
    dump_received
done
sleep 6
dump_received

failed=0

present() {
    if grep -q "$1" "$received"; then
        echo "  ok      present $2"
    else
        echo "  FAILED  missing $2" >&2
        failed=1
    fi
}

absent() {
    if grep -q "$1" "$received"; then
        echo "  FAILED  LEAKED  $2" >&2
        failed=1
    else
        echo "  ok      absent  $2"
    fi
}

echo "== asserting against telemetry emitted by real services =="

# Both processes exported, under the names ADR-0006 requires. A service whose
# resource never left the process is the failure this catches.
present '"screening-api"' 'screening-api resource'
present '"screening-worker"' 'screening-worker resource'

# 🔒 The whole slice ran, not only the HTTP hop. These come from the sample own
# ActivitySource, so they also prove ActivitySources registration.
present 'screen application' 'the worker span "screen application"'
present 'kyc.applications.submitted publish' 'the producer span'
present 'kyc.applications.submitted process' 'the consumer span'

# 🔒 Meter registration. Before AddMeter was wired, these incremented
# in-process and reached nothing — with a green build and green tests. This
# assertion is the reason this script exists.
present 'screening.applications.screened' 'the worker counter'
present 'screening.duration' 'the worker histogram'

# 🔒 ADR-0005's trace-context metric, which is a control only if it is
# subscribed — an instrument the provider never registered records in-process
# and reaches nothing, exactly like the counters above once did. .NET 10 starts
# W3C, so the value here is 0; the assertion is that the series arrives at all.
# The value-1 case is the .NET Framework default and needs the Phase 2 fixture.
present 'raksawi.telemetry.trace_context.corrected' 'the trace-context gauge'

# Declared Class 2 keys, allowed on spans.
present '"application.id"' 'application.id on a span'
present '"correlation.id"' 'correlation.id on a span'

# 🔒 Enumerated messaging keys. messaging.* is admitted key by key rather than
# by prefix, so these prove the enumeration covers what the NATS instrumentation
# actually emits — the failure being guarded against is an allowlist tightened
# against the specification instead of against a real span, which drops
# telemetry silently and looks identical to instrumentation not running.
present '"messaging.system"' 'the enumerated messaging.system'
present '"messaging.destination.name"' 'the enumerated messaging.destination.name'
present '"messaging.nats.message.subject"' 'a NATS-specific enumerated key'

# 🔒 CouchDB redaction, on a real span rather than a unit test Uri (ADR-0023).
# The document identifier must not survive; the redacted URL must, because a
# CouchDB span without its URL has no diagnostic value.
present "couchdb:5984/kyc/{docid}" 'the redacted CouchDB url.full'
absent "kyc/$APP_ID" 'the CouchDB document identifier in url.full'

# 🔒 Class 3. Stored in CouchDB, and it must be in no signal at all.
absent "$CANARY" 'the applicant name anywhere in telemetry'

# 🔒 Rev 3 D2.1 rule 1: Class 2 is permitted on a span and refused as a metric
# dimension. Asserted on the datapoint rather than the whole file, because
# application.id is legitimately present above.
if grep 'screening.applications.screened' "$received" | grep -q '"application.id"'; then
    echo "  FAILED  LEAKED  application.id as a metric dimension" >&2
    failed=1
else
    echo "  ok      absent  application.id as a metric dimension"
fi

# 🔒 The Class 2 key that actually travels is messaging.message.id, not the
# declared message.id, which nothing emits. It is permitted on a span above and
# must be refused as a metric dimension like every other Class 2 identifier.
if grep 'screening.applications.screened' "$received" | grep -q '"messaging.message.id"'; then
    echo "  FAILED  LEAKED  messaging.message.id as a metric dimension" >&2
    failed=1
else
    echo "  ok      absent  messaging.message.id as a metric dimension"
fi

# 🔒 Logs (ADR-0028). The worker writes one line with two structured
# properties: application.id, which is a declared Class 2 key, and Outcome,
# which is named the way .NET templates are usually named and matches no
# family and no declaration. One survives and one does not, from the same
# record, which is the whole rule in one assertion pair.
present 'Screened {application.id}' 'the worker log record'

# Scoped to the export request carrying that record, the way the metric
# dimension check above is scoped: application.id is legitimately present on a
# span, so a whole-file grep would prove nothing about the log.
logline=$(grep 'Screened {application.id}' "$received")

if echo "$logline" | grep -q '"application.id"'; then
    echo "  ok      present a declared Class 2 key on a log"
else
    echo "  FAILED  missing a declared Class 2 key on a log" >&2
    failed=1
fi

if echo "$logline" | grep -q '"Outcome"'; then
    echo "  FAILED  LEAKED  an undeclared log property" >&2
    failed=1
else
    echo "  ok      absent  an undeclared log property"
fi

# 🔒 The NATS scrape (ADR-0030). Broker health cannot come from instrumentation
# — nothing of ours runs in NATS — so this proves the one path it does have.
present 'gnatsd_varz_connections' 'a NATS broker series from the scrape'
present 'gnatsd_varz_slow_consumers' 'the core-NATS falling-behind signal'

# 🔒 The exporter publishes its own Go runtime next to the broker's health.
# Stored under a job named "nats" those read as broker health, which inverts
# the exact failure dashboard 3 exists to catch.
absent 'go_memstats_heap_alloc_bytes' "the exporter's own Go runtime"
absent 'promhttp_metric_handler_requests' "the exporter's own scrape counters"

# The mapping, scoped to a scraped datapoint: messaging.system is legitimately
# present on the spans above, so a whole-file grep would prove nothing.
if grep 'gnatsd_varz_connections' "$received" | grep -q '"messaging.system"'; then
    echo "  ok      present messaging.system mapped onto the NATS scrape"
else
    echo "  FAILED  missing messaging.system on the NATS scrape" >&2
    failed=1
fi

# server_id restates the scrape URL on every series and is dropped at the
# receiver. It is also the only label carrying a URL, so a leak is visible.
absent '"http://nats:8222"' 'the scrape URL as a datapoint label'

# 🔒 The collector's own health (Rev 3 I3.8, ADR-0030). level: detailed had
# produced these all along and carried them nowhere; the assertion is that they
# now leave the process, which is the whole difference between a metric and a
# control. Asserted at the sink, so it also proves the self-scrape survives the
# allowlist it is subject to.
present 'otelcol_receiver_accepted_spans' 'spans accepted, from the self-scrape'
present 'otelcol_exporter_queue_size' 'exporter queue depth'
present 'otelcol_process_memory_rss_bytes' 'collector memory'

# The same endpoint restates those one layer down as the OTLP receiver's own
# http_server_* and the gRPC exporter's rpc_client_*. They disagree with the
# otelcol_ series at the edges, so panelling either is a coin toss.
absent 'http_server_request_duration' "the receiver's own HTTP server metrics"
absent 'rpc_client_duration' "the exporter's own gRPC client metrics"

# 🔒 up is synthesised per scrape target — 1 reachable, 0 not. It is NOT subject
# to metric_relabel_configs, so both keeps above let it through, and it is the
# only signal that says a scrape target died rather than went quiet. Verified
# here because it is load-bearing for the dead-target half of I3.9 and it is
# not obvious from either scrape job's configuration that it survives them.
present '"name":"up"' 'the per-target scrape health series'

# Carve-outs, on real HTTP spans this time. CouchDB is reached with a Basic
# credential, so the header carve-out is doing real work here.
absent '"http.request.header.authorization"' 'the Authorization header'
absent 'admin:password' 'CouchDB credentials'

if [ "$failed" -ne 0 ]; then
    echo >&2
    cp "$received" ./e2e-instrumented-received.json 2>/dev/null || true
    echo "e2e-instrumented.sh: FAILED. Received telemetry copied to" >&2
    echo "./e2e-instrumented-received.json" >&2
    exit 1
fi

echo "OK"
