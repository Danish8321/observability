# D0.5 — Baseline MTTR

**Status:** not started
**Satisfies:** Rev 3 **D0.5**
**Consumed by:** Rev 3 **Gate 4** — the acceptance criterion for the whole project

---

## Definition

Recorded three ways from the same five incidents used in
[the decomposition](./incident-decomposition.md), because a single number chosen
now gets compared rigorously in eight weeks.

- **Mean** time to resolve
- **Median** time to resolve
- **Every individual number**, listed

A mean over five incidents is dominated by one outlier: if one ran fourteen hours
and four ran twenty minutes, the average describes nothing, and Gate 4 could show
"improvement" purely by not having an outlier that quarter. A median over five is
one incident's number, and it hides the long-tail cases this project should help
most with. Recording all three costs nothing and prevents both readings.

**Also record diagnose-stage time separately.** That is the stage this project
actually moves. If total MTTR is flat at Gate 4 while diagnose time halved and a
different stage worsened, only the separated number reveals it — and Rev 3's
instruction at Gate 4 is precisely to go back to the decomposition and find where
the time is really going.

## Worksheet

| # | Date | Total time to resolve | Diagnose-stage time |
|---|---|---|---|
| 1 | | | |
| 2 | | | |
| 3 | | | |
| 4 | | | |
| 5 | | | |

**Mean:** ______  **Median:** ______
**Mean diagnose:** ______  **Median diagnose:** ______

**Recorded on:** ______
**Incident window covered:** ______ to ______

## Reading it at Gate 4

Re-measure with the same definition, over the incidents that occurred since.
Compare all four figures, not one.

**Five incidents is not a statistically meaningful sample.** Eight weeks later
there will be another handful, and any comparison is directional at best. A 15%
movement in either direction proves nothing. A halving of diagnose-stage time
across several incidents is evidence; a 10% change in the mean is noise.

This is not a reason to skip the measurement. It is a reason to state the
uncertainty now, while nobody has a stake in the answer, rather than at Gate 4
when someone does.

Rev 3's warning applies unchanged: if MTTR has not moved, the diagnosis in D0.1
was wrong. Go back to the decomposition. **Do not declare success on coverage —
coverage was never the goal.**
