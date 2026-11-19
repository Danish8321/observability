# 43. Log fields are open unless a service opts into strict

Date: 2026-11-19

## Status

Proposed. Needs the plan owner's acceptance, because it deviates from Rev 3
**D2.1** (default-deny classification) for log attributes. On acceptance it
joins the deviation list in `README.md`.

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
`Message` (free text that may carry one).

## Decision

Log attributes are filtered in one of two modes, chosen per service.

- **Open, the default.** A log field is kept unless:
  - its name, ignoring case, is on a blocklist (`body`, `to`, `from`, `cc`,
    `email`, `password`, `secret`, `token`, `authorization`, `cookie`,
    `api_key`, among others; the list lives in `docs/allowlist.md`); or
  - its value looks like an email address, card number, IBAN or JWT. Then the
    value is replaced with `[redacted]` and the field is kept.

  The same value patterns run over the log body. This is plan item D9b's pattern
  scan, now applied to both modes.
- **Strict, by opting in.** Today's allowlist, unchanged. A service opts in
  with `Governance:Required=true` (plan contract C3). The package then sets the
  resource attribute `raksawi.governance=required`, and the collector applies
  `keep_matching_keys` only to logs from resources carrying it. An agent-
  instrumented service opts in through `OTEL_RESOURCE_ATTRIBUTES`.
- **In-process.** In open mode, core does not filter log attributes. In strict
  mode, the in-process log allowlist runs as today.
- **Counting.** The collector counts log fields removed and values redacted,
  per service. It cannot count them per key (see Consequences).

Circle is not regulated and runs in open mode.

## Consequences

- A service's logs carry its fields from the first deployment, with no
  vocabulary and no placeholder renames. Circle's friction 4 is closed by
  configuration rather than by a treasury pack.
- 🔒 **It fails open.** A regulated service that forgets `Governance:Required`
  is protected only by the blocklist and the value patterns. A field with an
  innocent name holding a national ID, a name or an address passes, which is
  exactly the case ADR-0003 cites (`applicantIdentifier` carrying a CPR). The
  controls against this are outside the code: onboarding states that regulated
  services must opt in, and the KYC policy package sets the flag itself, so a
  KYC service can't miss it.
- Agent-instrumented services lose ADR-0009's fail-closed collector posture
  for logs unless their deployment sets the resource attribute.
- Pattern scanning is a compensating control (plan, "Log bodies and exception
  text"). It recognises shapes, not meaning. A card-number pattern will also
  redact some long numeric identifiers. The patterns are reviewed with the
  allowlist, and false positives are corrected there.
- The collector has no per-key dropped-attribute metric, so in open mode
  `raksawi_telemetry_attributes_dropped` reports only what the package
  dropped in-process: nothing for logs. A count per service is the most the
  collector gives.
- `raksawi.governance` joins the resource families (ADR-0026), or the
  collector's resource allowlist strips the marker before the log rules can
  read it.

## Evidence required before Accepted is implemented

- `test-fast.sh`: a core-only service's log fields leave the process
  unfiltered, and a service with `Governance:Required=true` still has them
  filtered.
- `contract.sh`: the collector's strict log rule equals the declared allowlist
  for marked resources only.
- `e2e-instrumented.sh`: a log field from an unmarked service reaches the
  collector's output, a blocklisted one doesn't, and an email-shaped value
  arrives as `[redacted]`. The same field from a marked service is dropped.
