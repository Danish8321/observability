# 7. Correlation is minted at workflow start, seeded by the browser

Date: 2026-08-10

## Status

Accepted

## Context

Rev 3 **D2.3** defines `correlation.id` as scoping the whole business workflow
and surviving across traces, then requires behaviour to be defined for
redelivery, retry, DLQ, queue-group fan-out, and request/reply — without
defining any of it.

Rev 3 **D2.13** has the browser generating a UUID on page load and sending it as
`X-Correlation-Id`, which solves a specific and real problem: without it, each
AJAX call from a `.cshtml` page starts a fresh root span and a single user
action fragments into unlinked traces.

The two are not the same identifier. A page load is a UI event; a workflow is a
business fact. One page may run several workflows, one workflow may outlive
several page loads, and a scheduled NATS consumer has a workflow and no browser
at all.

## Decision

Two identifiers, distinct lifetimes:

- **`session.id`** — minted by the browser on page load, sent as
  `X-Correlation-Id`, recorded server-side. Solves AJAX fragmentation.
- **`correlation.id`** — minted by a service when a business workflow begins,
  and propagated unchanged for the life of that workflow, across traces and
  across NATS hops.

Where a workflow begins in a browser request, the originating `session.id` is
recorded alongside the new `correlation.id` so the two are joinable.

Both are opaque UUIDs, Class 2, and carry no user data. The browser-supplied
value is parsed as a `Guid` and rejected if it does not parse, per Rev 3
**N-D7** — an unvalidated header is an uncontrolled attribute value.

Behaviour in the five scenarios D2.3 names follows from the above:

| Scenario | `correlation.id` | `message.id` | `causation.id` | `trace_id` |
|---|---|---|---|---|
| Redelivery of the same message | unchanged | unchanged | unchanged | new |
| Application-level retry, republished | unchanged | new | original message | new |
| Message routed to DLQ | unchanged | unchanged | unchanged | new |
| Pub/sub fan-out to several consumers | unchanged, shared | unchanged | unchanged | one per consumer |
| Request/reply | unchanged | new on the reply | the request message | may continue |

## Consequences

- The model now has five identifiers where D2.3 specified four. This is a real
  cost and was accepted because collapsing them means either a correlation that
  is too coarse to mean "workflow" or an AJAX fragmentation problem left unsolved.
- Services must know what a workflow start is. That is a domain judgement, not a
  library rule, so the library provides the minting helper and the domain
  decides where to call it. A service that never calls it inherits the
  `session.id` and degrades to D2.13 behaviour rather than to nothing.
- 🔒 DLQ preserving `correlation.id` is the property that makes a dead message
  traceable back to the workflow that produced it. Without it, a message in the
  DLQ is an orphan and the incident question "which application did this belong
  to" is unanswerable.
- Business correlation does not depend on sampling, as D2.3 requires: every row
  above changes `trace_id` freely while `correlation.id` survives.
