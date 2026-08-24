Status: open

# Compliance backend's Ocelot gateway has no route configured for /health

Found 2026-08-24 reviewing a production debug/trace log export danish shared
(compliance-backend log, ~3 min window, 2026-08-19T14:19-14:22Z), while
checking whether it carried anything useful for Q4/Q5/Q9's closed
"no historical data" findings. It didn't add incident/throughput data, but it
surfaced this live gateway misconfiguration.

## What the log shows

Every `/health` probe through the gateway during the captured window fails
the same way:

```
Ocelot.DownstreamRouteFinder.Middleware.DownstreamRouteFinderMiddleware[0]
  Upstream url path is /health
Ocelot.Errors.Middleware.ExceptionHandlerMiddleware[0]
  Error Code: UnableToFindDownstreamRouteError
  Message: Failed to match Route configuration for upstream path: /health, verb: GET.
Ocelot.Responder.Middleware.ResponderMiddleware[0]
  ... setting error response for request path:/health, request method: GET
```

100% of the traffic in this log slice is `/health` probes, and 100% of them
404 through the gateway with `UnableToFindDownstreamRouteError`. No other
upstream path appears in the capture, so nothing else can be said about the
gateway's routing table from this evidence alone — only that `/health`
specifically has no configured downstream route.

## Why it matters

If this reflects current production config (not confirmed — this is a single
~3-minute log snapshot, not a live check), the gateway's own health surface
is silently broken: whatever's probing it (load balancer health check,
monitoring, orchestrator liveness probe) is getting 404s it may or may not be
correctly interpreting as "unhealthy." That's exactly the kind of gap this
project's coverage/health-signal work (Rev 3 D0.4b, this repo's estate
inventory D0.3) is meant to catch — but it was found by accident, reading a
debug log, not by any detector.

## Not yet done

- Confirm live whether the route is actually still missing (this log is a
  snapshot, not current state).
- Identify what's on the other end of the health probe (load balancer?
  orchestrator?) and what it does with a sustained 404.
- Decide whether this is a same-day platform fix or something this project's
  estate inventory should just record and move on from.

## Comments
