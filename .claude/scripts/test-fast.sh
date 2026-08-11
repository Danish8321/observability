#!/usr/bin/env sh
# Unit tests. No collector, no store, no network.
set -eu

cd "$(dirname "$0")/../.."

dotnet test --nologo -c Release
