#!/usr/bin/env sh
# Proves collector policy and the declared allowlist express the same rules.
#
# Blocked, not implemented: the allowlist has no code-side declaration yet.
# ADR-0017 puts the source of truth in assembly attributes; docs/allowlist.md
# says explicitly "concrete key list not validated" (Gate 2, ADR-0018), and no
# package in src/ emits those attributes today. deploy/collector/config.yaml
# says the same thing from the other side: "The allowlist processor that
# ADR-0003 requires is NOT here."
#
# There is nothing to diff yet. A real contract.sh needs, at minimum:
#   1. a reader that extracts the declared allowlist from assembly attributes
#      (families + carve-outs) — does not exist
#   2. a reader that extracts the enforced key set from the collector's
#      attribute-filtering processor — the processor itself does not exist
#   3. a comparison that fails on any key the collector allows that the
#      allowlist doesn't declare, or vice versa
#
# This script fails loudly instead of faking a pass, per the project's
# evidence rule. Replace this file when the allowlist is declared in code and
# the collector gains an attribute-filter processor (see docs/adr/0017,
# docs/adr/0018, docs/allowlist.md).
set -eu

echo "contract.sh: no code-side allowlist declaration and no collector-side" >&2
echo "allowlist processor exist yet (see comment at the top of this script for" >&2
echo "what each side is missing). There is nothing to compare. Not a pass." >&2
exit 1
