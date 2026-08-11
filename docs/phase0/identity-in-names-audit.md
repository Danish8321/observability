# D0.4b / D2.2 — Identity in routes and subjects

**Status:** not started
**Satisfies:** Rev 3 **D0.4b** (HTTP route templates) and **D2.2** (NATS subject taxonomy)

> Do this now, while it is a reading exercise. After Phase 2 it becomes a
> data-deletion exercise.

---

## The rule

**Names must not encode identity.** One rule, two layers.

| Layer | Wrong | Right |
|---|---|---|
| HTTP route | `/api/applicant/{cpr}` | `/api/applicant/{id}` with an opaque id |
| NATS subject | `kyc.screen.{email}` | `kyc.screen` |

The identifier must be opaque — never a CPR, document number, passport number,
email, or phone number, in a path segment or a query string.

## Why it bites harder than it looks

HTTP instrumentation captures the URL, so identifiers land in span attributes.
Where `http.route` fails to resolve to a template, they land in span *names* —
producing 🔒 regulated data in telemetry **and** unbounded cardinality from the
same defect.

`NATS.Net` v3 raises the cost on the messaging side: the subject now appears in
span attributes, in span-derived metric dimensions, *and* in
`messaging.destination.template`.

## Scope

The URL risk sits on the **AJAX endpoints**, not the page routes. The legacy
applications serve `.cshtml` pages whose behaviour lives in JavaScript, calling
backend endpoints over HTTP. A page URL rarely carries an identifier; an AJAX
endpoint frequently does.

Determine first **which runtime owns each endpoint** — a .NET 10 API or a 4.8 MVC
controller returning JSON — because that decides who owns the fix. The estate
inventory records this.

## Worksheet — routes

| Endpoint / route template | Owning service | Owning runtime | Identity-bearing? | What identifier | `http.route` resolves to template? | `url.query` carries identity? | Remedy owner | Target date |
|---|---|---|---|---|---|---|---|---|
| | | | | | | | | |

## Worksheet — NATS subjects

| Subject pattern | Publisher | Consumers | Identity-bearing? | What identifier | Remedy owner | Target date |
|---|---|---|---|---|---|---|
| | | | | | | |

## Per-endpoint confirmations

For every endpoint, confirm all three:

- [ ] `http.route` resolves to the route template, not the raw path
- [ ] `url.query` and `url.full` are dropped or redacted at the collector
- [ ] No route definition carries an identity-bearing segment

## Remedy

**Fix the name. Redact at the collector as an interim.**

Collector redaction goes in immediately, so telemetry is safe from day one.
Route and subject changes proceed on their own schedule, since they are published
contracts and changing them breaks callers. Each interim redaction rule is
removed when the last caller has moved.

**Every interim redaction rule has a named owner and a target date.** Without
them it becomes permanent, and permanent collector redaction is not the same
control: it fixes telemetry and leaves the underlying exposure untouched.

Collector redaction is explicitly **not sufficient on its own**. An identifier in
a URL is also in IIS logs, reverse-proxy logs, and browser history, none of which
the collector touches. Rev 3 **I3.2** holds that the collector is the net, not
the primary control, and this is a case where the distinction is concrete.

## 🔒 If the audit finds regulated data in a name

A CPR, passport number, or document number in a route template is an **existing
exposure today**, independent of this project. It is in web server logs now.

That finding does not belong on the observability backlog. It goes to the data
protection owner under
[ADR-0019](../adr/0019-delegated-data-protection-ownership.md), and it may be
more urgent than anything else in this plan.
