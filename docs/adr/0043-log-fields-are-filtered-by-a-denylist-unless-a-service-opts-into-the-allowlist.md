# 43. Log fields are filtered by a denylist unless a service opts into the allowlist

Date: 2026-11-19

## Status

Accepted 2026-11-19 by the plan owner, as written. Not yet implemented.
Deviates from Rev 3 **D2.1** (default-deny classification) for log attributes,
listed in `README.md#deviations-from-rev-3`. D6a and D7 stay in the plan.

Amends [ADR-0003](./0003-runtime-allowlist-at-source.md) and
[ADR-0028](./0028-logs-are-enforced-in-process-on-net10-only.md) for log
attributes, and [ADR-0009](./0009-governing-agent-instrumented-services.md) for
agent-instrumented services' logs. Span, span-event and resource attributes are
unchanged.

## Context

Every log field must be declared before it is stored. A field is kept only if
its key is in an assembly signed with `raksawi.snk` (ADR-0017) and matches the
collector's `keep_matching_keys(log.attributes, …)` (`deploy/collector/config.yaml`).

Circle measured the cost (circle-integration ticket 02). It has 60
`[LoggerMessage]` methods and 57 placeholders, and every one is dropped twice:
in-process by the log allowlist processor, then at the collector. Getting them
through means a signed vocabulary package with a key per field, renaming each
placeholder to a dotted key (which `[LoggerMessage]` rejects, SYSLIB1014), and
a collector regex release. For a large service that is months of work, done
before the first useful log line, and the vocabulary will still be incomplete.
Services won't adopt the packages on those terms.

Most of those fields are harmless: `AccountId`, `EntryId`, `Topic`, `Kind`. The
risky ones are few and recognisable by name or by value shape. Circle's are
`{To}` (an email address in `NoOpEmailNotifier`) and `{Body}`, `Detail`,
`Message` (free text that may carry one). Listing what to remove is a much
smaller job than listing everything that may stay.

## Decision

Log attributes are filtered by a denylist by default, or by the existing
allowlist for a service that opts into it.

### Denylist, the default

A log field is kept unless one of these applies:

- **The estate denylist.** Its name, ignoring case, is on the estate denylist
  (`body`, `to`, `from`, `cc`, `email`, `password`, `secret`, `token`,
  `authorization`, `cookie`, `api_key`, among others). The list lives in
  `docs/allowlist.md`, beside the allowlist, and is reviewed the same way. The
  package and the collector both enforce it.
- **The service's own denylist.** Its name, ignoring case, is in the service's
  `Observability:Logs:DeniedKeys` (plan Q3). For example, circle could add
  `RawStatus` or `ProviderDetail`. These entries are enforced in-process only.
  - They need no signing or provenance check. ADR-0017 guards the allowlist
    because a key there lets data out; a denylist entry can only remove data,
    so anyone may add one.
  - The collector doesn't see them until the policy generator (D9a) emits
    them. An agent-instrumented service has no in-process step, so its entries
    go in the collector config directly.
- **The value patterns.** Its value looks like an email address, card number,
  IBAN or JWT. The value is replaced with `[redacted]` and the field is kept.
  The same patterns run over the log body at the collector. This is plan item
  D9b's pattern scan, now applied to both modes.

### Allowlist, by opting in

Today's allowlist, unchanged.

- A service opts in with `Governance:Required=true` (plan contract C3).
- The package then sets the resource attribute `raksawi.governance=required`.
  The collector applies `keep_matching_keys` only to logs from resources
  carrying it, plus the value patterns.
- An agent-instrumented service opts in through `OTEL_RESOURCE_ATTRIBUTES`.
- The KYC policy package sets `Governance:Required` itself.

### In-process

- With the denylist, core removes the estate denylist and the service's
  `DeniedKeys`, and nothing else.
- With the allowlist, the in-process log allowlist runs as today.

### Counting

- In-process removals are counted on `raksawi_telemetry_attributes_dropped` by
  key, as today.
- The collector counts fields it removed and values it redacted per service,
  not per key (see Consequences).

Circle is not regulated and uses the denylist.

## Consequences

- A service's logs carry its fields from the first deployment, with no
  vocabulary and no placeholder renames. Its first filtering work is to add the
  handful of names it knows are risky to `DeniedKeys`. Circle's friction 4 is
  closed by configuration rather than by a treasury pack.
- 🔒 **It fails open.**
  - A regulated service that forgets `Governance:Required` is protected only
    by the two denylists and the value patterns. A field with an innocent name
    holding a national ID, a name or an address passes. That is exactly the
    case ADR-0003 cites (`applicantIdentifier` carrying a CPR).
  - The controls against this are outside the code. Onboarding states that
    regulated services must opt in, and a KYC service can't miss it because the
    KYC package sets the flag.
- Agent-instrumented services lose ADR-0009's fail-closed collector posture
  for logs unless their deployment sets the resource attribute.
- Pattern scanning is a compensating control (plan, "Log bodies and exception
  text"). It recognises shapes, not meaning. A card-number pattern will also
  redact some long numeric identifiers. The patterns are reviewed with the
  denylist, and false positives are corrected there.
- The collector has no per-key metric for what it removes. A field caught only
  at the collector is counted per service, not per key.
- `raksawi.governance` joins the resource families (ADR-0026). Otherwise the
  collector's resource allowlist strips the marker before the log rules can
  read it.

## Evidence required before this is called implemented

- `test-fast.sh`:
  - a core-only service's log fields leave the process except those on the
    estate denylist or in `DeniedKeys`, ignoring case;
  - a service with `Governance:Required=true` still has the allowlist applied.
- `contract.sh`:
  - the collector's allowlist log rule equals the declared allowlist and
    applies to marked resources only;
  - its denylist rule equals the estate denylist in `docs/allowlist.md`.
- `e2e-instrumented.sh`:
  - a log field from an unmarked service reaches the collector's output;
  - a denylisted field doesn't, and nor does one in that service's
    `DeniedKeys`;
  - an email-shaped value arrives as `[redacted]`;
  - the same harmless field from a marked service is dropped.
