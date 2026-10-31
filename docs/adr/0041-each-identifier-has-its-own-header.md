# 41. Each correlation identifier has its own header

Date: 2026-10-31

## Status

Accepted — amends [ADR-0007](./0007-correlation-lifetime.md), deviates from
Rev 3 D2.13

## Context

[ADR-0007](./0007-correlation-lifetime.md) separated `session.id` (one browser
page load) from `correlation.id` (one business workflow), but kept Rev 3
**D2.13**'s wire format: the browser sends its page-load UUID as
`X-Correlation-Id`.

The reference service never did that. `CorrelationMiddleware` reads the session
from `X-Session-Id` and continues the workflow from `X-Correlation-Id`, echoing
the latter back so a caller can quote it. The ADR, `CONTEXT.md` and the
onboarding guide said one thing; the only working code said another, and
nothing recorded the difference.

The ADR's wire format cannot work alongside its own decision. If
`X-Correlation-Id` carries the session, there is no header left for a caller to
continue a workflow on, and the echoed value a support ticket quotes would be a
page load rather than the workflow the ticket is about.

ADR-0007 also required the browser-supplied value to be parsed as a `Guid` and
rejected otherwise (Rev 3 **N-D7**). Nothing parsed it.

## Decision

- **`X-Session-Id`** carries `session.id`. Minted by the browser on page load.
- **`X-Correlation-Id`** carries `correlation.id`. Supplied by a caller
  continuing a workflow, otherwise minted where the workflow begins, and echoed
  in the response.
- Both values are recorded only if they parse as a `Guid`.
  `KycTelemetry.SetSessionId` and `SetCorrelationId` ignore anything else, so
  every service gets the check rather than each middleware repeating it. A
  supplied `X-Correlation-Id` that is not a Guid is treated as absent and a new
  one is minted, because it is echoed, stored and published downstream as well
  as tagged.

**Deviation from Rev 3 D2.13**, which names `X-Correlation-Id` as the header for
the browser's page-load UUID. The reason is ADR-0007's: these are two
identifiers with two lifetimes, and one header cannot carry both.

## Consequences

- Browser code written to D2.13 sends its session in `X-Correlation-Id`. A
  server following this ADR treats that as a workflow continuation: it
  continues a "workflow" that is really a page load, and records no
  `session.id`. Nothing in the estate is in production yet, so the fix is to
  send `X-Session-Id` from the start; any browser code already written to D2.13
  needs the header renamed.
- 🔒 An identifier the caller controls is an attribute value the caller
  controls. Rejecting non-Guid values bounds both identifiers to 32 hex digits
  of nothing, whatever a caller sends.
- A non-Guid session is dropped silently. That costs AJAX grouping for that
  page, never business work (Rev 3 I3.6).
