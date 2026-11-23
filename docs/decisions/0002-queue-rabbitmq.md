# ADR 0002: RabbitMQ as the work queue, via raw RabbitMQ.Client

**Status:** Accepted

## Context
URLs to crawl are distributed across N workers. A worker can be killed mid-job (`docker kill`), and
its work must not be lost. Messages that keep failing must be parked, not retried forever.

## Decision
Use RabbitMQ 4 with **quorum queues** and a **dead-letter exchange**, through the raw
`RabbitMQ.Client` 7 library with manual acks.

## Rejected alternatives
- **Redis Streams.** Unacked entries stay in the pending list until someone reclaims them, so
  recovery needs hand-written `XPENDING` / `XAUTOCLAIM` logic, and there is no built-in DLQ.
- **MassTransit.** It hides the ack/redelivery mechanics the report has to explain, and MassTransit
  v9 is commercially licensed.

## Consequences
- When a consumer's connection drops, RabbitMQ redelivers its unacked messages to another worker
  automatically. That covers the `docker kill` scenario.
- Quorum queues support a delivery limit; poison messages go to the DLX after N attempts.
- We write the topology declaration, consumer loop and ack/nack handling ourselves (more code, fully visible).
- One more container to run. The management UI (port 15672) helps in the demo.
