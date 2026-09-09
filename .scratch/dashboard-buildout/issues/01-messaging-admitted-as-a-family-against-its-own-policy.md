Status: closed — 2026-09-08

# `messaging.*` was admitted as a family, against the policy stated three lines above it

`docs/allowlist.md` has carried a stability policy since
[ADR-0018](../../../docs/adr/0018-allowlist-composition.md) marking the
messaging convention **experimental — enumerate individually**, on the argument
that it has been renamed repeatedly upstream and a family prefix would let a
rename through unreviewed.

The implementation did the opposite. `"messaging."` was a member of
`AllowlistRules.AllowedFamilies`, and both collector keep statements carried
`messaging\\.` in the family alternation. All three enforcement points
(ADR-0003) admitted the whole prefix.

Found while enumerating the keys for dashboard 3, not by a gate. Nothing in
`contract.sh` could have caught it: the contract test asserts the two sides say
the same thing, and they did — both were wrong in the same way.

## Impact

Any key beginning `messaging.` reached storage without review, on every signal,
from every service including agent-instrumented ones with no analyzer and no
in-process processor. A rename or an invention upstream would have arrived as
telemetry that quietly changed shape rather than as a dropped-key count and a
review, which is precisely the outcome the policy exists to prevent.

No leak is known to have resulted — the semantic convention's messaging keys are
Class 0 or 1 with the single exception of `messaging.message.id` (issue 02). The
finding is the absent control, not a disclosure.

## Fix

`AllowlistRules.AllowedMessagingKeys` — thirteen keys, exact match, consulted in
`IsAllowedByFamily` *before* the family loop. `messaging.` removed from
`AllowedFamilies`. Both collector keep statements spell all thirteen out.

Twelve of the thirteen are what the two reference services actually emitted,
dumped at the sink on 2026-09-08 (ADR-0018's empirical half).
`messaging.consumer.group.name` is the thirteenth, admitted ahead of use so
dashboard 3 is not blocked on an allowlist review when the instrumentation
lands.

## Verification

`contract.sh` — 102 passed, including
`Messaging_is_not_admitted_as_a_family_on_either_side`, which asserts the
absence on both sides rather than the presence of the enumeration. A test
asserting only that the thirteen keys are present would still pass with the
family prefix restored alongside them.

`e2e-instrumented.sh` — the enumerated keys observed at the sink on telemetry
from real services, so the enumeration is verified against what the NATS
instrumentation emits rather than against the specification.

## Comments
