#!/usr/bin/env sh
# Proves collector policy and the declared allowlist express the same rules.
#
# Both sides now exist. The code side is AllowlistRules.cs plus the
# [assembly: AllowedAttributeKey] declarations (ADR-0017); the collector side is
# transform/allowlist in deploy/collector/config.yaml (ADR-0009). They are two
# statements of one rule, because the collector has to govern services that
# contain none of our code, so the drift they can develop is the thing worth
# testing.
#
# CollectorAllowlistContractTests reads the shipped config file — not a copy —
# and asserts every family, every carve-out, and every never-a-metric-dimension
# key from the code side appears on the collector side, that the keep is the
# last span statement (so anything unnamed is gone by default), and that
# error_mode is propagate (so an erroring statement drops the batch rather than
# passing attributes through unfiltered).
#
# What this does NOT prove: that either side is correct. Rev 3 Gate 3 verifies
# redaction by inspecting stored data, which is e2e.sh's job, not this one.
set -eu

cd "$(dirname "$0")/../.."

echo "== allowlist contract: code side vs collector side =="
dotnet test --nologo -c Release \
    --filter "FullyQualifiedName~CollectorAllowlistContractTests"

echo "== collector config validate =="
if command -v otelcol >/dev/null 2>&1; then
    otelcol validate --config=deploy/collector/config.yaml
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    storage_dir="$(mktemp -d)"
    trap 'rm -rf "$storage_dir"' EXIT
    MSYS_NO_PATHCONV=1 docker run --rm \
        -v "$(pwd)/deploy/collector":/etc/otelcol \
        -v "$storage_dir":/var/lib/otelcol/storage \
        otel/opentelemetry-collector-contrib:0.140.1 \
        validate --config=/etc/otelcol/config.yaml
else
    # The contract tests above compare text. Only otelcol itself can say the
    # OTTL parses, so a missing validator is a failure here rather than a skip.
    echo "Neither otelcol nor a running docker daemon is available, so the" >&2
    echo "OTTL in transform/allowlist is unvalidated. Not a pass." >&2
    exit 1
fi

echo "OK"
