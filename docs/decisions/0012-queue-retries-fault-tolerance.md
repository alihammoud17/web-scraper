# ADR 0012: Queue topology, retries and fault tolerance

**Status:** Accepted (refines ADR 0002 and ADR 0004)

## Context
Worker containers are stateless and share only RabbitMQ, PostgreSQL and Redis. Any of them can be killed at any
moment (`docker kill`), stopped (`docker compose stop`), or scaled up and down. A site can time out, answer 429
with `Retry-After`, return 503, or fail forever. Whatever happens, no task may be lost, none may be stored twice,
and a site must never be hammered harder because something failed.

## Decision

### Topology
```
 API StartCrawl / RetryDeadLetter ─┐
 Worker (child tasks) ─────────────┤ publish rk "task" (persistent, publisher confirms, mandatory)
                                   ▼
                    ┌───────── crawl  (direct exchange) ─────────┐
                    └──────────────────┬─────────────────────────┘
                                       ▼
        crawl.tasks  quorum · x-delivery-limit 5 · DLX → crawl.dlx · at-least-once dead-lettering
                                       │  prefetch 1 · manual ack (after DB commit)
                                       ▼
                               Worker consumers (N)
            retryable failure:         │                give up (attempts exhausted / permanent error):
            publish rk "<tier>"        │                publish to crawl.dlx with reason headers
                    ▼                  │                           ▼
   crawl.retry (direct exchange)       │       crawl.dlx (fanout) ◄── broker: delivery limit exceeded,
    ├ rk 5s   → crawl.retry.5s   ─┐    │                │                 or nack(requeue=false)
    ├ rk 30s  → crawl.retry.30s  ─┤ quorum, x-message-ttl = tier,       ▼
    ├ rk 2m   → crawl.retry.2m   ─┤ no consumers, DLX → crawl  crawl.dead (quorum)
    └ rk 10m  → crawl.retry.10m  ─┘ rk "task": expired → back          │
                                    to crawl.tasks                      ▼
                                                     DeadLetterRecorder (worker): dead_letter_entries
                                                     + task DeadLettered, then ack
```
- Quorum queues with `x-dead-letter-strategy: at-least-once` (and the `x-overflow: reject-publish` it requires): a
  message leaves a queue only once the target queue has confirmed it, so nothing is lost between queues.
- Tier queue names include the delay, so changing `QUEUE_RETRY_DELAYS` creates new queues instead of failing on a
  changed queue argument.
- We run one RabbitMQ node, so quorum queues give no replication here; we still use them for durability, the
  delivery limit and at-least-once dead-lettering. A production setup would run three nodes.

### Delayed retries: per-tier queues, not per-message TTL
RabbitMQ only expires messages **at the head of a queue**. With per-message TTL on one delay queue, a 10-minute
message at the head blocks a 5-second message behind it until the 10 minutes pass (**head-of-line blocking**):
retry delays become unpredictable and short retries starve. With one queue per delay tier, every message in a
queue has the same TTL, so expiry order equals arrival order and the head is always the next one due.
`Retry-After` picks the first tier that is long enough (above the largest tier, the largest is used).

Retries happen at two levels:
1. **Inside one attempt** (P4's Polly pipeline): short blips, seconds, exponential backoff **with jitter**,
   `Retry-After` up to 60 s; every try takes a rate-limit token.
2. **Through the queue** (this ADR): longer outages; the worker is freed, the retry survives restarts, and the
   attempt number travels in the message. Tier delays are fixed (no jitter); a burst of retries still can't hurt
   a site because the shared token bucket spaces every request.

### Processing: ack only after the commit
For each message (prefetch 1, manual ack), `ProcessCrawlTask`:
1. claims the task (`status NOT IN (Succeeded, Skipped, DeadLettered)`; a stale `InProgress` left by a killed
   worker may be claimed again). If the task is already finished, this is a duplicate: re-publish its still-queued
   children and ack.
2. fetches politely (scope → robots.txt → shared rate limit → HTTP);
3. in **one transaction**: upserts the page, inserts the fetch (unique per task), upserts the links, inserts the
   child tasks (unique per job and URL; they remember their parent), sets the task's status — and commits;
4. marks the children seen in Redis, publishes them (confirmed), checks whether the job is complete;
5. only then the consumer **acks**. A retry or dead letter is published (confirmed) before the ack.

The child rows act as an **outbox**: they are committed together with the parent's success, so if the worker dies
after the commit, the parent is redelivered, found finished, and its queued children are published again.

### Error classification
| Outcome | Decision |
|---|---|
| 2xx, 304, 3xx (target enqueued at the same depth) | succeed |
| 404, 410 | succeed, page marked removed (`removed_at`; never deleted) |
| robots.txt disallow, out of scope, unknown site | skip (permanent, not a failure) |
| 408, 429, 500, 502, 503, 504, network error, timeout, database/cache briefly down | retry until `MaxAttempts`, then dead-letter |
| other 4xx, response too large | dead-letter at once |
| unknown exception (a bug) | reject (`nack`, no requeue) → the broker dead-letters it |

### Dead letters
Every path into `crawl.dead` (a worker gave up, the delivery limit, a rejection) is recorded by one consumer, the
`DeadLetterRecorder`, as a `dead_letter_entries` row (URL, last error, attempt count). `POST
/dead-letters/{taskId}/retry` re-queues the task from attempt 1 and reopens its job.

### Shutdown
- **SIGTERM** (`docker compose stop`): cancel the subscription, finish and ack the in-flight task, close (host
  shutdown timeout 25 s, under compose's 30 s grace period). Past the deadline the task is abandoned unacked.
- **SIGKILL** (`docker kill`): the connection drops and the broker redelivers the unacked task to another worker.

### Sequence: a worker crash and its recovery
```
API            RabbitMQ crawl.tasks        Worker A                 Worker B                 Postgres
 │ POST /crawls     │                          │                        │                        │
 │──INSERT job+T1 (one txn) ───────────────────────────────────────────────────────────────────►│ T1 Queued
 │──publish T1 ────►│ (persistent, confirmed)  │                        │                        │
 │                  │──deliver T1 (prefetch 1)►│                        │                        │
 │                  │                          │──TryStart T1 ─────────────────────────────────►│ T1 InProgress, attempts 1
 │                  │                          │  fetch (rate-limited)  │                        │
 │                  │      ✗ docker kill A (SIGKILL): connection drops, T1 was never acked       │
 │                  │ T1 back to Ready         │                        │                        │
 │                  │ (redelivered, delivery-count 1)                   │                        │
 │                  │──deliver T1 ─────────────────────────────────────►│                        │
 │                  │                          │                        │──TryStart T1 ─────────►│ stale InProgress → restart, attempts 2
 │                  │                          │                        │  fetch                 │
 │                  │                          │                        │──ONE TXN: page upsert, │
 │                  │                          │                        │  fetch (UQ task), links,│
 │                  │                          │                        │  children (UQ job,url), │
 │                  │                          │                        │  T1 Succeeded ─COMMIT─►│
 │                  │◄─publish children (confirmed)─────────────────────│                        │
 │                  │◄─────────────────────── ack T1 (only after the commit) ─│                  │
Variant: if A had died AFTER its commit but before its ack, B's TryStart finds T1 Succeeded
(duplicate) → re-publishes T1's still-Queued children → ack. Nothing lost; unique keys prevent duplicate rows.
```

## Rejected alternatives
- **Per-message TTL on one delay queue:** head-of-line blocking (above). **The delayed-message-exchange plugin:**
  not in the official image, and delayed messages aren't replicated.
- **Ack on receipt:** a crash loses the task. **Ack before publishing the children:** a crash loses the subtree.
- **A prefetch above 1:** a killed worker takes several unacked tasks with it, and one slow page holds back the
  others it has already received. Throughput is capped by the per-site rate limits anyway.
- **Counting retries only with `x-delivery-limit`:** delayed retries republish the message (resetting the count).
  In RabbitMQ 4 a `nack` with requeue doesn't count either; only deliveries returned because the consumer died do.
  So the delivery limit is our **crash-loop guard** (a message that keeps killing its workers is dead-lettered after
  5 deliveries), and retries are counted by us.
- **A separate transactional-outbox table and relay:** more moving parts for the same guarantee here.
- **Exactly-once delivery:** RabbitMQ can't provide it; at-least-once plus idempotent writes is simpler and provable.
- **Writing dead letters only where a worker gives up:** broker-side dead letters would have no row, and their tasks
  would stay `InProgress` forever.
- **Marking URLs seen (Redis `SADD`) before the database insert:** a rolled-back transaction would leave URLs marked
  seen but never stored. Seen is now marked after the commit; Postgres's unique `(job, url)` is the arbiter.

## Consequences
- Proven by `scripts/demo-worker-kill.sh`, `demo-timeout.sh`, `demo-429.sh` and `demo-dlq.sh`, and by integration
  tests against real RabbitMQ (redelivery, delivery limit, rejection, graceful stop) and PostgreSQL (completing the
  same task twice leaves the same rows), which run in CI.
- A crash mid-fetch sends the site a second request for that page (rate-limited; the database still has one row).
- A long database or cache outage walks tasks through the tiers (about 12.5 minutes with the defaults) into the
  dead-letter queue, from where they can be retried.
- Queue-level delays aren't jittered; the shared rate limiter and in-attempt jitter cover what jitter would protect.
