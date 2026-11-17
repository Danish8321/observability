# Delivery gap

**Means:** in the last ten minutes the worker finished (screened or abandoned)
fewer than 90% of the applications the API accepted with a `202`, with at
least 20 accepted. Every submitter was told their application was received.

Core NATS keeps no backlog, so there is no queue depth to read (panel 3.2).
The gap is read at the two ends instead. Three causes look the same here:

- **Behind:** the worker is slower than arrivals. Messages wait in the
  client's buffer and are screened late.
- **Dropping:** the buffer is full (16,384 messages by default) and NATS.Net
  drops the newest. Those applications are never screened, and nothing else
  records it.
- **Stopped:** no worker is subscribed. The broker discards every message
  published while it is down.

**First query** (Mimir): finished as a share of accepted, over ten minutes.

```promql
sum by (deployment_environment_name) (increase({__name__=~"screening_applications_(screened|abandoned)"}[10m])) / sum by (deployment_environment_name) (increase(http_server_request_duration_count{http_route="/applications", http_request_method="POST", http_response_status_code="202"}[10m]))
```

Empty while accepted is not: nothing was finished at all. The worker is
stopped or not reporting.

Then tell the three apart:

- Is the worker reporting? `screening_duration_count{job="kyc/screening-worker"}`.
- Is it slow? See [Screening slow](./screening-slow.md). A slow worker that keeps
  up with nothing else wrong is "behind".
- Did the broker drop anyone? See [Slow consumer](./slow-consumer.md).

**Likely causes**

- Screening is slow (provider or CouchDB) and arrivals outpace one worker.
- The worker is down or restarting. Messages published meanwhile are lost.
- A burst filled the client buffer and NATS.Net dropped messages.

Lost applications stay `received` in CouchDB. Finding and resubmitting them is
a service task; moving the sample to JetStream (plan M10) is what makes this
recoverable.

**Owner:** the screening service's team.
