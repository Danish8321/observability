# Onboarding: infra / platform

For whoever runs the collector, backend store, and estate-wide instrumentation config. Not for service integration ([`integration.md`](./integration.md)) or library development ([`developer.md`](./developer.md)).

## Mental model first

Three enforcement points, default deny at each ([ADR-0003](../adr/0003-runtime-allowlist-at-source.md), [ADR-0009](../adr/0009-governing-agent-instrumented-services.md)):

1. **Analyzer** at build — catches ungoverned attribute keys in code you don't own.
2. **Library** before export — runtime allowlist, source redaction (e.g. CouchDB URLs).
3. **Collector** before storage — the only control that reaches processes containing none of our code (the "agent path", auto-instrumented .NET Framework services). This must be **fail-closed**: an ungoverned service exports nothing rather than exporting ungoverned data. That's deliberate — it converts an invisible compliance gap into a visible coverage gap you already have a detector for.

You are the last line for anything not source-controlled. Nothing reaches storage without your policy applying to it.

## What you own

- Collector deployment and config (`deploy/collector/`, `deploy/docker-compose.yaml` for demo shape — **not** the production shape, see below).
- Collector-side redaction/allowlist enforcement matching the declared policy (`transform/allowlist` in `deploy/collector/config.yaml`, live since 2026-08-25). `contract.sh` fails if it and the code-side `AllowlistRules` drift, so the manual check against `docs/allowlist.md` is no longer the only thing standing between the two.
- Free-text scanning at the collector — interpolated log strings are banned at build (ADR-0004) but the collector is the backstop for anything that slips through.
- Agent-path governance: for .NET Framework services with no package reference, MSI auto-instrumentation + `Register-OpenTelemetryForIIS` + per-app-pool env vars is host config you run, and the collector is the *only* place their telemetry gets governed.
- Estate inventory / coverage denominator: the service register is the coverage denominator ([ADR-0021](../adr/0021-service-register-is-the-coverage-denominator.md)) — reconciled against reality, not aspirational.

## Protocol and ports

- **OTLP http/protobuf on 4318.** Port 4317 (gRPC) is deliberately closed estate-wide — gRPC is unsupported on the .NET Framework 4.8 target, so services never send it. Don't open 4317 "just in case."

## Demo infra (what exists today)

`deploy/docker-compose.yaml` — NATS (JetStream flag on, but demo only uses core NATS — no redelivery), CouchDB, and the collector. SigNoz is deliberately **not** in this compose file (avoids drift from upstream) — install it separately, then bring this stack up:

```sh
curl -fsSL https://signoz.io/foundry.sh | bash   # SigNoz's docker-compose install is deprecated; use Foundry
docker compose -f deploy/docker-compose.yaml up -d
```

SigNoz's Foundry ingester already owns host port **4318**, so this repo's own collector is remapped to **4319** in `deploy/docker-compose.yaml` (host side only — the collector still listens on 4318 inside its container, and estate-wide production is still 4318). Point service `Otlp__Endpoint` at `http://localhost:4319` when running samples against this compose stack. See `samples/README.md` for the full worked sequence, including `Screening.Api`'s actual port (5206).

🔒 This compose file is dummy-data-only and ships with plaintext demo credentials (CouchDB admin/password). **Do not treat any part of this as a production template** — sampling is 1.0 and there is no auth hardening (ADR-0022).

The allowlist itself is *not* demo-scoped any more: `transform/allowlist` is applied in this config, fails closed (`error_mode: propagate`), and is the same policy production will run. What remains demo-shaped is everything around it — exporter target, storage sizing, credentials, sampling.

## Sampling and boot-failure behavior

Absent sampler config is a **production boot failure** by design ([ADR-0010](../adr/0010-sampling-defaults.md)) — a service won't silently start with a wrong/no sampling rate outside Development. If a service fails to boot citing `SamplingRatio`, that's the library working as intended, not a bug to route around — the service team needs to set it, not you.

Telemetry itself must never fail a business request (Rev 3 I3.6) — batch export only, bounded timeouts, no synchronous flush on the request path. Any control that would cost unbounded work on the request path belongs at the collector, not in-process. If you're asked to add a synchronous check into a service's request path for governance reasons, that's the wrong layer — push it to collector config instead.

## Governance source of truth

The allowlist is declared as assembly attributes in policy packs, read by both analyzer and runtime — no separate manifest, nothing to drift ([ADR-0017](../adr/0017-allowlist-declared-as-assembly-attributes.md)). Your collector-side rules need to express the *same* families/carve-outs as `docs/allowlist.md`. When that doc changes, your collector config changes in the same review — don't let them diverge silently.

Data classes 3 (restricted PII) and 4 (secrets) must appear **nowhere** — not even reaching the collector is the goal, but collector-side scanning is still the backstop for anything a source-side control misses (e.g. free text, per ADR-0004).

## Verification you're responsible for

| Script | Proves | Status |
|---|---|---|
| `check.sh` | build/format only, not infra | exists |
| `test-fast.sh` | unit tests only | exists |
| `test-full.sh` | above + `otelcol validate` on collector config | exists |
| `contract.sh` | collector policy and declared allowlist express the same rules | exists — the code/collector comparison passes; the `otelcol validate` step **fails** unless `otelcol` or a running docker daemon is available, by design rather than skipping |
| `e2e.sh` | assertions against *received* telemetry, not configuration — required because Rev 3 Gate 3 needs redaction verified against stored data, not intent | exists — nine assertions pass (2026-08-25); needs a running docker daemon and **fails** without one, by design |

`contract.sh` compares text and `otelcol validate` parses the OTTL. Neither watches a span go through. Don't claim redaction or allowlist enforcement is "in place" from either. `e2e.sh` is the one that does: it stands up the shipped `deploy/collector/config.yaml` **unmodified** in front of a sink container named `signoz-ingester-1`, posts OTLP/JSON probe telemetry, and greps what the sink received. Editing the config it tests would prove nothing, so it is not templated or overlaid.

🔒 Resource attributes are allowlisted too, as of 2026-08-26, on a narrower family set than spans get ([ADR-0026](../adr/0026-resource-attributes-are-allowlisted-narrowly.md)): identity, provenance, and where it ran — nothing from `http.`, `db.`, `url.`, `messaging.` or `exception.`. This matters most for a service you configure by `OTEL_RESOURCE_ATTRIBUTES`: set a key outside those families and it is dropped, silently, on every signal. If a resource attribute you expected is missing from the store, check it against the resource table in [`docs/allowlist.md`](../allowlist.md) before assuming the collector is broken.

One operational note the demo stack shares: a fresh docker named volume is root-owned and the collector image runs as uid 10001, so its `file_storage` queue directory has to be chowned before first start. `e2e.sh` does this with a throwaway busybox container — the collector image is distroless and has no shell.

## Manual verification checklist (today)

1. Collector logs show received spans (port 4318 estate-wide; **4319** against this repo's demo compose — Foundry's own ingester holds 4318 there)
2. One service reaches the backend end to end
3. Two services show up on one trace (HTTP propagation across the hop)
4. A message hop (NATS) lands on the same trace — most likely failure point (confirmed live 2026-08-11: the worker's process span was starting a disconnected new root trace instead of joining the producer's — fixed, but re-verify this on any consumer span you add)
5. Inspect a stored CouchDB-touching span: `url.full` should show the redacted path, not the raw document ID **and not userinfo/credentials** — an earlier redaction bug stripped the doc ID but left credentials in cleartext (confirmed live 2026-08-11, fixed) — exact-host-match redaction **fails open**, so a wrong host list means silent non-redaction, not an error
6. Query by `correlation.id` and confirm the whole workflow assembles — this is the actual deliverable, not span count

## Reading before touching production shape

- `docs/adr/0009-governing-agent-instrumented-services.md` — agent path governance
- `docs/adr/0010-sampling-defaults.md` — sampling boot-failure rule
- `docs/adr/0021-service-register-is-the-coverage-denominator.md` — coverage math
- `docs/adr/0023-couchdb-changes-the-database-surface.md` — why CouchDB URL is the risk surface, not statement text
- `docs/allowlist.md`, `docs/diagnostic-queries.md` — the two documents your config must stay in sync with
- `docs/open-questions.md` — check before assuming a production collector policy is fully specified; regulatory/SSO decisions are still open and gate parts of this
