#!/usr/bin/env sh
# Build and static-analysis gate. Warnings are errors (Directory.Build.props),
# so a clean run here means the compilation is clean on every target framework.
set -eu

cd "$(dirname "$0")/../.."

echo "== restore =="
dotnet restore

echo "== build (all target frameworks) =="
dotnet build --no-restore -c Release

echo "== format =="
dotnet format --verify-no-changes

echo "OK"
