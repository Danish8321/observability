# Demo plan — prove the value before spending the budget

**Goal:** a live demonstration to stakeholders that this shortens incident
diagnosis. Not a tour of a telemetry UI.

**Budget:** 8 working days to demo. A 5-day cut line is marked below.

**Status:** plan only, nothing started.

---

## The one thing the demo must do

Not "here are traces." Stakeholders have seen dashboards and were not convinced
by them.

**Show the same failure diagnosed twice.** Once the way it is done today, once
with the stack. On the clock, in front of them.

| | Today | With the stack |
|---|---|---|
| Method | Log files on N hosts, opened by hand, timestamps compared manually | One search |
| What you get | "Service B threw an exception" | The full path of one request across every service it touched, with the failing hop highlighted |
| Time | Measured live, or from the incident decomposition | Measured live |

The number on the right is the pitch. Everything below exists to produce it
honestly.

**Pick the failure scenario before building anything.** The scenario dictates
which services get instrumented, and nothing else does. Best candidate is a real
past incident that was painful and is reproducible — the audience already knows
it hurt, so nothing has to be argued.

## What is deferred, and the exact point it stops being safe

Deferred by decision, 2026-08-10: the regulatory request, the DPO briefing, the
Gate 3 sign-off, access tiers, the allowlist analyzer, the store bake-off. The
compliance team exists and the organisation owns the data, so these are
sequencing, not omissions. The ADRs stay accepted and unimplemented.

**The line: none of this touches production KYC traffic until the deferred work
lands.** The demo runs on a staging or synthetic workload.

That is not a compliance ritual. Telemetry written to a store is written; if it
turns out to contain a CPR, the remedy is deleting a datastore rather than
changing a config. Staging costs nothing here because the demo needs a
*reproducible* failure, and production incidents are not reproducible on demand.

Everything else is genuinely reversible and is deferred without argument.

## What changes because the database is CouchDB

This was not known when the earlier documents were written, and it is not a
detail.

**Good news, and it shortens the demo.** CouchDB is HTTP/JSON. Calls to it are
captured by standard `HttpClient` instrumentation as ordinary client spans — no
database instrumentation package, no `db.*` semantic conventions, no work. The
entire SQL client instrumentation problem disappears.

**The bad news is in the same fact.** With SQL, sensitive text hid in
`db.statement`, and the defence was to drop it. With CouchDB it hides in the
URL:

| Shape | Risk |
|---|---|
| `GET /kyc/{docid}` | The document ID is in `url.full`. If document IDs are CPRs or contain them, every CouchDB call leaks identity into telemetry |
| `GET /kyc/_design/x/_view/y?key="..."` | View keys are in the query string, and a key is very often the thing being looked up by |
| `POST /kyc/_find` | Mango selector is in the **body** — not captured by default, and must stay that way |

**So the identity-in-names audit is now the first technical task, not a Phase 0
worksheet item, and it has a third input: CouchDB document ID and view key
shape.** One question decided the demo's redaction work: *are document IDs
derived from applicant data, or are they opaque?*

**Answered 2026-08-11 (QD2): opaque.** There is almost nothing to do —
`CouchDbUrlPolicy` redaction stays in as defense-in-depth, not as a
compliance-blocking fix. It still fails open on a host mismatch, and that
should be verified against a real span before being relied on (see
`.scratch/demo-readiness/issues/01-verify-couchdb-redaction-real-span.md`).

**One more CouchDB-specific trap.** The `_changes` feed is a long-poll: a single
HTTP request held open for minutes. Instrumented naively it produces enormous
spans that wreck every latency percentile on the dashboard being demoed. Filter
it explicitly, or the demo shows a p99 of four minutes and the conversation ends
there.

## Phases

### Phase D0 — decide and preserve (1 day)

Small, and two items in it cannot be recovered later.

- [ ] **Pick the failure scenario.** A real past incident, reproducible in
      staging, spanning at least two services and one NATS hop. Write down the
      diagnosis path used at the time and how long it took
- [ ] **Name the services in that path.** Three at most. This is the whole scope
- [ ] 🔒 **Answer the CouchDB document ID question.** Opaque or derived
- [ ] **One-hour baseline snapshot** of the demo services — CPU, working set,
      p99 from IIS logs or Kestrel. Not the one-week Run 0, just enough that
      "does it slow things down" has an answer. **This expires the moment
      anything is instrumented**, and it costs an hour
- [ ] Confirm `NATS.Net` version on those services is ≥ 3.0.1

### Phase D1 — the spine (3 days)

The thinnest thing that carries a trace end to end.

- [ ] Collector running. OTLP `http/protobuf` on **4318**
- [ ] **SigNoz**, single system, chosen for the demo without a bake-off. One
      thing to run beats four while proving a point. Reversible — the queries
      are specified store-neutrally in
      [`../diagnostic-queries.md`](../diagnostic-queries.md), so the bake-off
      still happens later and loses nothing
- [ ] Service 1 (.NET 10) instrumented: ASP.NET Core, `HttpClient`, NATS
- [ ] Service 2 instrumented, same
- [ ] `_changes` feed excluded from tracing
- [ ] 🔒 `url.full` redaction on CouchDB spans **if D0 said document IDs are
      derived**
- [ ] **One trace, visible, crossing both services and the NATS hop**

That last line is the gate. Until a trace crosses a NATS boundary in the UI,
nothing else matters — and it is the step most likely to fail, because it is
where `traceparent` propagation actually gets tested.

> **5-day cut line.** If day 5 arrives and the trace does not cross NATS, demo
> two HTTP services only and say so. A working HTTP-only demo beats a broken
> distributed one. Do not cut Phase D2 to save Phase D1.

### Phase D2 — the scenario (2 days)

- [ ] Reproduce the chosen failure in staging, on demand, repeatably
- [ ] Build **only** the panels the scenario needs. Not four dashboards —
      probably two: a service overview and a trace search
- [ ] Rehearse the diagnosis end to end and time it
- [ ] Rehearse the "today" path too, and time that honestly. An unfair
      comparison will be spotted and it destroys the argument

### Phase D3 — the demo (1 day, mostly rehearsal)

- [ ] Run it live. Break it in front of them
- [ ] State the cost plainly: what is built, what is not, what production
      rollout needs, what the deferred compliance work is
- [ ] **Ask for a specific decision**, not for approval in general

## What .NET Framework 4.8 does in this plan

Nothing, unless the chosen scenario requires it.

[ADR-0012](../adr/0012-net10-first-sequencing.md) already sequences .NET 10
first. If the failure scenario crosses a 4.8 service, that service is the demo's
main risk — `ActivityIdFormat` defaults to Hierarchical there, and a trace
silently splits in two rather than failing loudly. Budget a day for it and set
the two lines that fix it:

```csharp
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;
```

Prefer a scenario that avoids 4.8. If the point is to convince people, do not
lead with the hardest runtime.

## What the demo deliberately does not have

State these rather than hoping nobody asks. Being asked and having an answer is
the position that wins.

| Missing | Why it is fine for now | When it lands |
|---|---|---|
| Allowlist enforcement | Staging data only | Before production traffic |
| Access tiers | Two people can see it | Before production traffic |
| Store bake-off | SigNoz picked for one demo, queries are portable | Phase 3 |
| Sampling policy | 100% at demo volume | Before production traffic |
| The 4.8 estate | Not needed to prove value | Phase 2 |
| One-week baseline | One-hour snapshot answers the overhead question | Before rollout |

## Ask at the end

Two answers, not applause:

1. **Is this worth continuing?** If no, it cost 8 days instead of two months —
   that is the point of running it this way
2. **If yes:** the compliance work starts *then*, and it has an eight-week
   horizon that runs in parallel with the build rather than before it. That is
   how the timeline gets absorbed instead of paid twice
