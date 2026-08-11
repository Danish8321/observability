#!/usr/bin/env sh
# Everything test-fast.sh proves, plus static validation of collector config.
# No collector process, no store, no network — otelcol validate parses and
# type-checks the config without starting a pipeline.
set -eu

cd "$(dirname "$0")/../.."

echo "== unit tests =="
dotnet test --nologo -c Release

echo "== collector config validate =="
if ! command -v otelcol >/dev/null 2>&1; then
    echo "otelcol not on PATH. Install the same distribution deploy/docker-compose.yaml" >&2
    echo "uses (otel/opentelemetry-collector-contrib) or run via its container, e.g.:" >&2
    echo "  docker run --rm -v \"\$(pwd)/deploy/collector\":/etc/otelcol \\" >&2
    echo "    otel/opentelemetry-collector-contrib:0.140.1 validate --config=/etc/otelcol/config.yaml" >&2
    exit 1
fi
otelcol validate --config=deploy/collector/config.yaml

echo "OK"
