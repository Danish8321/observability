# Failure matrix and the durability bound

**Status:** not started. Rev 3 **I2.3**, **I3.6**, **I3.7**.
**Sequencing:** the restore test (**I3.7**) runs **first**. The queue is sized
from its measured duration.

---

## The bound is chosen, then measured

Rev 3 asks for the real durability bound to be recorded. That bound is not
discovered by testing — it is a direct consequence of the collector's persistent
queue capacity, which is a number somebody picks.

**Sizing basis: the measured restore window, plus margin.**

The worst realistic backend outage is a restore from backup. Rev 3 **I3.7**
already requires that restore to be tested, so its duration is a measured figure
rather than an imagined one. Everything else — a process restart, an upgrade, a
disk-full recovery — is shorter.

Rejected alternatives: a small buffer loses the telemetry from the incident that
caused the outage; an unbounded queue is undefined behaviour with a disk-full
incident attached, and **I3.6** requires drop behaviour to be *defined* and
alerted.

A long queue drains slowly, so a larger capacity means a longer and larger
duplicate-spike artifact after recovery. That is a cost of this choice, not a
surprise.

## Sequence

1. Test **restore** from backup. Record wall-clock duration. Rev 3 **I3.7** — a
   backup that has never been restored is not a backup
2. Size `file_storage` queue capacity to that duration plus margin, at measured
   peak ingest
3. Record the resulting capacity in time and in bytes
4. Run the matrix below
5. Brief on-call, using the wording under *The guarantee*

## The matrix

| Scenario | Expected | Observed | Notes |
|---|---|---|---|
| Collector process restart | No loss, queue replays | | Actually restart it |
| Collector node failure | Loss bounded to that node's in-memory batch | | |
| Backend restart | No loss | | |
| Backend down 5 minutes | No loss | | |
| Backend down 30 minutes | Loss beyond queue capacity — **record the number** | | |
| Backend down for the full restore window | No loss, if sizing is correct | | The test of step 2 |
| Collector disk full | Defined drop behaviour, alert fires | | |
| Network partition app ↔ collector | Loss, best-effort — accepted | | |

## The guarantee

Verbatim, for the on-call brief:

> **At-least-once from collector ingress to backend, for outages shorter than the
> configured queue capacity.**

Not "zero loss". The difference matters at 3am.

Two consequences to brief explicitly:

**At-least-once means duplicates.** Replay produces repeated spans, so
span-derived metrics spike as an artifact, not an incident. **This is a test, not
a note:** drain a backlog and confirm that no dashboard or alert raises a false
incident. Every panel in
[`../diagnostic-queries.md`](../diagnostic-queries.md) derived from span counts
is in scope.

**Everything upstream of the collector is best-effort.** Application-side
buffering is not worth it here, and Rev 3 **I3.6**'s invariant is why: telemetry
must never participate in business request success or failure. A buffer inside
the application is a step toward exactly that.

## Also confirmed here

- [ ] Block the collector at the firewall — the API still serves. Rev 3 **N-I2**
- [ ] `OTEL_SDK_DISABLED=true` plus restart fully disables telemetry, no redeploy. Rev 3 **D3.4**
- [ ] Dead man's switch distinguishes all four states. Rev 3 **I3.8**
- [ ] Cold-tier query actually returns. Rev 3 **I3.4**
