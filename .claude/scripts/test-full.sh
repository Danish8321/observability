#!/usr/bin/env sh
# Everything test-fast.sh proves, plus static validation of collector config.
# No collector process, no store, no network — otelcol validate parses and
# type-checks the config without starting a pipeline.
set -eu

cd "$(dirname "$0")/../.."

echo "== unit tests =="
dotnet test --nologo -c Release

echo "== collector config validate =="
if command -v otelcol >/dev/null 2>&1; then
    otelcol validate --config=deploy/collector/config.yaml
elif command -v docker >/dev/null 2>&1; then
    echo "otelcol not on PATH, falling back to its container." >&2
    storage_dir="$(mktemp -d)"
    trap 'rm -rf "$storage_dir"' EXIT
    MSYS_NO_PATHCONV=1 docker run --rm \
        -v "$(pwd)/deploy/collector":/etc/otelcol \
        -v "$storage_dir":/var/lib/otelcol/storage \
        otel/opentelemetry-collector-contrib:0.140.1 \
        validate --config=/etc/otelcol/config.yaml
else
    echo "Neither otelcol nor docker on PATH. Install otelcol, matching the" >&2
    echo "distribution deploy/docker-compose.yaml uses" >&2
    echo "(otel/opentelemetry-collector-contrib), or install docker." >&2
    exit 1
fi

echo "OK"
