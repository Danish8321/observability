# Regulatory position request — observability platform

**Status:** draft, not yet sent
**Recipient:** *(to be confirmed — internal legal, compliance function, or external counsel)*
**Raised by:** observability platform work
**Satisfies:** Rev 3 **I0.1**

---

## What this is about

We are building a system that collects operational diagnostic data — traces,
metrics, and logs — from the applications that process KYC applications. The
purpose is to shorten the time it takes to diagnose production incidents.

This data is **not** intended to contain applicant personal data. The design
excludes it by default at three separate points, and we treat any personal data
appearing there as a defect rather than as a category to be managed. We are
nevertheless treating the platform as being in regulatory scope, because it
observes systems that are.

We need the positions below confirmed in writing so that decisions already taken
can be validated, and so that anything we have got wrong is corrected while it is
still cheap to correct.

## What we need from you

Six statements. For each: **confirm**, **correct**, or **not applicable**.
Then one open question at the end, which is the most important one.

---

### 1. Data residency

> All observability data — traces, metrics, and logs — remains within the country
> of operation. No component storing or processing it sits outside that country,
> including any managed or third-party service.

**Why we are asking.** This determines where the collector and storage may be
hosted. We have assumed the strictest position and are self-hosting entirely.

**If corrected:** a relaxation lets us reconsider managed services, which would
reduce operational burden. A tightening would require re-siting hosts, so we
would rather learn it now than after data has been stored.

---

### 2. Boundary of processing

> The collector and the telemetry store sit inside the regulated boundary, and
> automated inspection of diagnostic data within that boundary — for the purpose
> of detecting and removing personal data that reached it in error — is permitted.

**Why we are asking.** One of our safeguards scans free-text diagnostic content,
such as error messages, for patterns resembling personal data, in order to remove
it. That safeguard is only defensible if the scanning itself happens inside the
compliant boundary.

**If corrected:** we would need an alternative control for free text, and the
realistic alternative is discarding error message content entirely, which
materially reduces our ability to diagnose failures.

---

### 3. Separation from audit evidence

> Operational diagnostic data is never the system of record for regulatory or
> audit evidence. It may hold copies or references. Audit evidence is retained
> separately, with its own retention period and its own access control.

**Why we are asking.** We want to be certain that deleting diagnostic data on a
short cycle can never destroy something we were obliged to retain — and equally,
that a debugging tool does not fall into audit scope by accident.

**If corrected:** if any part of this data does constitute regulatory evidence,
its retention and access controls change fundamentally and it must not share
infrastructure with operational data.

---

### 4. Retention

> Operational diagnostic data is retained for 14 days in immediately queryable
> storage and up to 90 days in archival storage, after which it is deleted. No
> minimum retention obligation applies to it.

**Why we are asking.** These periods are chosen for engineering usefulness. We
need to know whether any obligation sets a floor (a minimum we must keep) or a
ceiling (a maximum we may keep).

**If corrected:** both directions are straightforward configuration changes,
provided we learn the number before storage is sized and provisioned.

---

### 5. Sign-off

> Before this platform reaches production, a named data protection owner
> countersigns an audit confirming that no restricted personal data appears in
> the collected data, on both application runtimes.

**Why we are asking.** We have structured this as a technical reviewer who
evaluates the detailed controls and a data protection owner who countersigns and
carries accountability. We need to know whether that split is acceptable to you
and who the countersigning person should be.

**If corrected:** tell us what form of sign-off you require and from whom.

---

### 6. Access to diagnostic data

> Diagnostic data is readable by engineers on call or debugging. It contains no
> personal data, but it does contain opaque reference numbers, which means a
> reader can see that *some* application progressed through *these* steps at
> *these* times, without being able to tell whose it is. Audit records are held
> separately, readable by a materially smaller group.

**Why we are asking.** We want to be explicit that this is a behavioural record
even though it identifies nobody. If that requires narrower access, restricting
it is straightforward — but it would slow incident diagnosis, which is the
purpose of the platform, so we would rather agree the position than assume it.

**If corrected:** tell us who should be able to read diagnostic data, and whether
opaque reference numbers change your answer.

---

### 7. The open question

> **Is there any obligation, restriction, or requirement bearing on this platform
> that the six statements above do not cover?**

This is the one we most need answered. The statements were written by
engineers reasoning from a technical plan. They describe the obligations we
thought to ask about. The obligation we did not think of is the one that will be
expensive, and it will be cheapest to hear about now, before anything is built.

---

## What we are doing meanwhile

We are proceeding on the assumption that every statement above is confirmed as
written, on the basis that these are the strictest readings available to us — so
that being corrected means relaxing a constraint rather than tightening one.

Relaxing is a configuration change. Tightening, after data has been collected,
could mean re-siting infrastructure and potentially a disclosure. That asymmetry
is why we would rather be told we have over-restricted than under-restricted.

No applicant data has been collected by this platform. Nothing is in production.
