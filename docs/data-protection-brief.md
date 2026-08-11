# Data protection brief

**For:** the two people named in
[ADR-0019](./adr/0019-delegated-data-protection-ownership.md) — the delegated
technical owner and the countersigning data protection officer.

**Purpose:** so that neither of you is asked to sign something in eight weeks
that you are hearing about for the first time.

---

## What is being built

A system that collects operational diagnostic data — traces, metrics, and logs —
from the applications that process KYC applications. It exists to shorten the
time taken to diagnose production incidents.

It is **not** a data store about applicants. It is a record of what the software
did. The distinction matters, and it is also the thing most likely to be
misunderstood in both directions.

## What it deliberately does not collect

Two categories never appear, by design:

- **Restricted personal data** — CPR, passport and MRZ data, names, email
  addresses, phone numbers, addresses, document numbers
- **Secrets** — tokens, passwords, credentials, authorisation headers,
  connection strings

Also never collected: request bodies, response bodies, and raw SQL statement
text.

This is enforced by default-deny, at three independent points: the compiler
refuses to build code that attaches an unapproved field; the application drops
unapproved fields before sending anything; the collector drops them again before
storage. Anything not explicitly approved is discarded rather than flagged.

## What it does collect, and why that still matters

Opaque reference numbers — an application reference, a tenant reference, a
workflow reference. These identify nothing on their own. They exist because
without them an incident cannot be reconstructed: you can see that something
failed, but not which piece of work it belonged to.

**The honest consequence:** someone with access can see that *some* application
moved through *these* steps at *these* times. That is a behavioural record, even
though it names nobody. We would rather state that plainly now than have it
discovered during an audit.

Access is split in two: diagnostic data readable by engineers on call, audit
records held separately and readable by a much smaller group. Operational
diagnostic data is never the system of record for audit evidence.

## What each of you is being asked to do

### Delegated technical owner

You approve the things whose correctness is a technical judgement, because they
cannot be evaluated without understanding what the software actually emits:

- The **carve-out list** — the specific fields excluded from otherwise-permitted
  groups. This is where the whole protection argument lives, and it is
  reviewed as a security artifact rather than as configuration
- New approved fields, and new opaque reference numbers
- Any field used to group metrics — an unbounded one degrades the metrics store
- Any use of context propagation, which is disabled by default because it
  spreads to every downstream system at once
- The technical findings of the pre-production audit, on both application
  runtimes

Your approvals are pull requests in this repository. They are reviewable after
the fact rather than taken on trust.

### Countersigning data protection officer

You countersign, and you own outright:

- The **pre-production sign-off**. Production does not proceed without it
- Any exception permitting SQL statement text to be captured. Default is refusal;
  an exception requires written approval, because an ordinary query such as
  `WHERE Cpr = '...'` is a reportable disclosure
- The regulatory position, once the written answer arrives — see
  [`regulatory-request.md`](./regulatory-request.md)
- The quarterly re-audits once the system is live

Where the two of you disagree, the countersignature is withheld and the change
does not proceed. Neither role approves alone.

## What we need from you now

1. **Confirm you accept the role.** Both roles must be named before the
   pre-production gate, and it has an eight-week horizon
2. **Read the regulatory request** and tell us who should receive it
3. **Confirm the restricted audit group.** Proposed: the compliance function,
   plus the delegated technical owner — two to four people, nobody else by
   default. The technical owner is included because approving what may be
   captured, while being unable to see what was captured, is a gap. Tell us if
   that is wrong, and tell us the names in the compliance function

## One thing that may need attention before any of this

An audit of endpoint addresses and message names is scheduled, checking whether
any of them contain identifying information — for example an endpoint whose
address includes a CPR number.

🔒 If that audit finds one, it is an existing exposure **today**, independent of
this project: such an address is already recorded in web server logs. That
finding would belong to you immediately, not to this project's schedule.
