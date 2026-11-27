# P5: Queue, workers and fault tolerance

- **Date:** 2026-11-27
- **Branch:** `p5-queue-workers` (to be created from `main`)
- **Goal:** Stateless worker containers that share only RabbitMQ, Postgres and Redis, ack only after
  the database commit, process tasks idempotently, and survive every failure scenario in the brief
  (worker killed mid-task, timeouts, 429 with `Retry-After`, always-failing URLs).
- **Status:** in-progress
- **Approval:** approved in plan mode

## Context
P4 (merged, PR #2) gave us a polite single-URL fetch pipeline, but nothing schedules work: there is
no queue topology, no consumer, and no database writes. P5 adds:
- the RabbitMQ topology (a quorum work queue, delayed retries, a DLX/DLQ);
- a consumer that **acks only after the database commit**;
- idempotent processing on top of P3's unique keys;
- a retry policy;
- a minimal frontier (decided: yes);
- `POST /crawls` and dead-letter retry;
- TestSite failure endpoints;
- one demo script per scenario.

Decisions taken while planning:
- Include a minimal frontier (link extraction, depth and page limits, child tasks).
- Short demo timings through optional env settings.
- Add `POST /dead-letters/{taskId}/retry`.

## Steps
- [x] 1. `docs: close out M4 and align docs for P5`
- [x] 2. `feat(domain): add task lineage, skipped status and dead-letter retry marker`
- [x] 3. `feat(application): add retry policy and task message contract`
- [ ] 4. `feat(application): add crawl store, queue and frontier ports with crawl use cases`
- [ ] 5. `feat(infrastructure): add Postgres crawl store with idempotent upserts`
- [ ] 6. `feat(infrastructure): add link extractor, crawl budget and seen-set batch operations`
- [ ] 7. `feat(infrastructure): declare RabbitMQ topology and confirmed task publisher`
- [ ] 8. `feat(worker): add crawl task consumer with ack after commit and graceful shutdown`
- [ ] 9. `feat(worker): add dead-letter recorder`
- [ ] 10. `feat(api): add POST /crawls, dead-letter retry and queue length in /health`
- [ ] 11. `ci: run Postgres and RabbitMQ integration tests against service containers`
- [ ] 12. `feat(testsite): add failure endpoints, editable pages and benchmark tree`
- [ ] 13. `feat(scripts): add fault-tolerance demo scripts`
- [ ] 14. `docs: add ADR 0012 and P5 manual tests`

Each step is one commit and also updates `docs/CHANGELOG.md`.

### Drift check (step 1, and wherever noted)
| # | Drift | Effect on P5 / fix |
|---|---|---|
| 1 | The brief says `docs/plan/`; plans live in `docs/plans/` | Save there |
| 2 | P4 is merged, but CLAUDE.md still shows M4 unticked, and the CHANGELOG `Unreleased` section mixes P4 items with the README reorganization | Step 1: tick M4. Fold the items into `[M4 / P4] … 2026-11-27 (PR #2)`. Start a new `Unreleased` |
| 3 | P3/P4 deferred to P5: the **upsert stores**, **depth/pagination rules**, **re-queueing when `Retry-After` > 60 s** | Stores and Retry-After re-queueing are in P5. Depth: plain link-hop depth. **Pagination-aware depth for GTA5-Mods stays P6** |
| 4 | ADR 0002: "poison messages go to the DLX after N attempts" via the delivery limit | Refined in ADR 0012. The **delivery limit** only catches crash/redelivery loops. **Retries are counted by us** (an attempt header plus `crawl_tasks.attempt_count`), because delayed retries republish the message |
| 5 | ADR 0004 / P4: `ISeenUrlStore.TryMarkSeenAsync` (SADD) as the check-and-mark | Add `FilterUnseenAsync` (SMISMEMBER) before the insert and `MarkSeenAsync` (SADD) **after the commit**. `crawl_tasks (crawl_job_id, url)` stays the arbiter |
| 6 | `CrawlTaskStatus` has no "skipped", and tasks don't know their parent | Add the `Skipped` status, `crawl_tasks.discovered_by_task_id` and `dead_letter_entries.retried_at` (migration) |
| 7 | `HealthResponse` has no queue length | Add `QueueLength` (ready messages in `crawl.tasks`) |
| 8 | Newswire is `FetchMode.Browser`, but no Playwright fetcher exists (P6) | `StartCrawl` rejects Browser sites with 400 |
| 9 | The TestSite seed (1 req/s, 100 pages) is too small for a 500+ page tree | Migration: `testsite` → **5 req/s** and **1000 pages**. The README rate proof uses `check-rate.sh 5` |

### Topology (steps 7–9; RabbitMQ 4, raw `RabbitMQ.Client` 7)
```
 API StartCrawl / RetryDeadLetter ─┐
 Worker (child tasks) ─────────────┤ publish rk "task" (persistent, publisher confirms)
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
- **Who declares what:** the API and the worker both declare the core topology (idempotent, identical arguments). Only the worker declares the retry tiers.
- **Tier queue names** include the delay, so changing `QUEUE_RETRY_DELAYS` creates new queues instead of failing with `PRECONDITION_FAILED`.
- **Overflow:** quorum + `x-dead-letter-strategy: at-least-once` + `x-overflow: reject-publish`, so nothing is lost between queues.
- **Single node:** quorum queues give no replication here. We still use them for durability, the delivery limit and at-least-once dead-lettering. Production would run 3 nodes.
- **Head-of-line blocking:** with per-message TTL on one delay queue, RabbitMQ only expires messages at the head, so a 10 min message blocks a 5 s message behind it. Per-tier queues (same TTL in each) expire in FIFO order, so there's no head-of-line blocking.
- **Two retry layers:**
  - **Layer 1** (P4 Polly, inside one attempt): seconds, jitter, `Retry-After` ≤ 60 s.
  - **Layer 2** (queue tiers): longer outages. The worker is freed and the attempt count is persisted.
  - **Jitter:** layer 2 delays aren't jittered. The shared token bucket spaces requests anyway.
  - **Retry-After:** picks the smallest tier ≥ the wait. Above 10 min, it uses the largest tier.

### Message and processing (steps 3–4)
- **`CrawlTaskMessage`** (JSON): `{ taskId, jobId, siteId, url, depth, attempt }`.
  - Persistent, `MessageId = taskId:attempt`, an `x-attempt` header.
  - On dead-letter: `x-last-error` / `x-reason` headers.
- **`RetryPolicy`** (pure) → `TaskDecision`:

| Outcome | Decision |
|---|---|
| 2xx, 304 | Succeed (304: page unchanged) |
| 3xx with a target | Succeed + enqueue the target (same depth) |
| 404, 410 | Succeed and **mark the page removed** |
| Blocked: robots, out of scope, unknown site | **Skip** (permanent, no DLQ) |
| 429, 503, 408, 500, 502, 504; `HttpRequestException`; timeout; DB/Redis unavailable | **Retry** in tier `max(backoff(attempt), Retry-After)`, until `attempt == MaxAttempts` → **DeadLetter** |
| Other 4xx, `ResponseTooLargeException` | **DeadLetter** at once |
| Unknown exception (a bug) | Consumer does `nack(requeue:false)` → the broker dead-letters it (`rejected`) |

- **`ProcessCrawlTask.HandleAsync(message) → TaskDisposition`**, one of `Ack` / `Retry(tier, error)` / `DeadLetter(reason)`:
  1. `TryStartTaskAsync`: guarded `UPDATE … WHERE status NOT IN ('Succeeded','Skipped','DeadLettered')`.
     - If no row changes, it's a **duplicate**: re-publish the task's `Queued` children and return `Ack`.
     - A stale `InProgress` can be restarted.
  2. Load the job and site. A cancelled job → Skip.
  3. `PoliteFetcher.FetchAsync`. Incremental mode passes the stored validators.
  4. Extract links and filter them: in scope, `depth+1 ≤ MaxDepth`, `FilterUnseenAsync`, `budget.TryReserveAsync` (Redis `INCRBY`).
  5. **One transaction** (`CompleteTaskAsync`):
     - upsert the page;
     - insert the fetch `ON CONFLICT (crawl_task_id) DO NOTHING`;
     - upsert links;
     - insert children `ON CONFLICT (crawl_job_id,url) DO NOTHING` with `discovered_by_task_id`;
     - update the task status;
     - **commit**.
  6. After the commit: `MarkSeenAsync(children)`, publish the `Queued` children (confirmed), `TryCompleteJobAsync`.
  7. Return `Ack`. The consumer acks **only now**.
  - **Retry:** the task → `Failed` with `last_error`, then `Retry`. **Dead-letter:** `DeadLetter` (the recorder writes the DB).
- **The child rows act as an outbox:** they're committed with the parent, and re-published if the parent is redelivered.
- **`StartCrawl(siteId, mode, seeds?)`:**
  1. Validate (the site exists, it isn't Browser, the seeds are in scope).
  2. One transaction: the job (`Running`) and the seed tasks.
  3. Publish (confirmed). On failure, the job → `Failed` and the API returns 503.
  4. Mark the seeds seen and reserve budget.
- **`RetryDeadLetter(taskId)`:** the task → `Queued`, `attempt_count = 0`, `retried_at = now()`, the job reopened, publish attempt 1.
- **`RecordDeadLetter`:** upsert `dead_letter_entries ON CONFLICT (crawl_task_id) DO UPDATE …, retried_at = NULL`, the task → `DeadLettered`, try to complete the job.

### Consumer and shutdown (steps 8–9)
- **Connection and channels:** one connection per worker (automatic recovery). A consume channel with `BasicQos(prefetch 1)` and manual ack. A publish channel with publisher confirms.
- **`CrawlTaskConsumer`** maps the disposition:
  - `Ack` → `BasicAck`;
  - `Retry` → confirmed publish to `crawl.retry` (rk = tier, `x-attempt+1`), then ack;
  - `DeadLetter` → confirmed publish to `crawl.dlx`, then ack;
  - unknown exception → `BasicNack(requeue:false)`.
- **`DeadLetterRecorder`:** consumes `crawl.dead`, calls `RecordDeadLetter`, then acks. It covers worker give-ups and broker dead-letters (`x-death`).
- **Graceful shutdown (SIGTERM):** `BasicCancelAsync` → finish the in-flight message and ack → close. `HostOptions.ShutdownTimeout = 25 s` (compose `stop_grace_period: 30s`). At the deadline, cancel **without an ack**, so the message is redelivered.
- **`docker kill` (SIGKILL):** the connection drops, and the unacked message goes back to Ready (`redelivered=true`). At most one per worker.
- **Heartbeat:** unchanged.

### Sequence: worker crash and recovery (for the report)
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

### Infrastructure, API, TestSite, scripts (steps 5–7, 10, 12, 13)
- **`PostgresCrawlStore`:** raw `INSERT … ON CONFLICT` and guarded `UPDATE`s in an EF transaction.
- **`AngleSharpLinkExtractor`:** `a[href]` and `<base href>`. Skips `mailto:`, `javascript:` and `rel=nofollow`. Normalizes with `NormalizedUrl.TryCreate` and keeps the link text.
- **`RedisCrawlBudget`:** `INCRBY crawl:{job}:budget n` → the allowed count (7-day expiry).
- **RabbitMQ:** `RabbitMqOptions`, `RabbitMqTopology` (name prefix for test isolation), `RabbitMqConnection`, `RabbitMqTaskQueue` (`ITaskQueue`, confirmed), `RabbitMqQueueStats` (queue length for `/health`).
- **API (no auth yet):**
  - `POST /crawls` `{ siteId, mode?, seeds? }` → 202 `{ jobId, seedCount }`, or 400/404/503;
  - `POST /dead-letters/{taskId}/retry` → 202, or 404/409;
  - `/health` + `queueLength`.
- **TestSite:**
  - `/delay/{ms}/…`;
  - `/flaky/{key}?status=429|503&failures=N&retryAfter=S`;
  - `/hang/{key}?hangs=N`;
  - `/fail/{key}` (+ `POST /fail/{key}/heal`);
  - `/editable/{id}` (ETag / Last-Modified / 304, `POST` to change);
  - `/tree/{path}?branch=8&depth=3` (585 pages).
- **Scripts** (`scripts/`, bash + `lib.sh`):

| Script | Does | Expected result (PASS) |
|---|---|---|
| `demo-worker-kill.sh` | 3 workers; crawl `/delay/2000/tree/…` (13 slow pages); `docker kill` the worker with an in-flight task; then a second run with `docker compose stop` | Job `Completed`. Tasks = pages = 13, each with exactly 1 fetch row. Another worker logs `redelivered=true`. 0 duplicates, 0 lost. The graceful stop logs "finished in-flight task, acked" |
| `demo-timeout.sh` | `CRAWLER_ATTEMPT_TIMEOUT_SECONDS=2`; crawl `/hang/{k}?hangs=2` then `/hang/{k2}?hangs=99` | First: succeeds inside one attempt (in-process retries). Second: queue-tier retries, then a `dead_letter_entries` row (reason "timeout") |
| `demo-429.sh` | Crawl `/flaky/{k}?status=429&failures=2&retryAfter=3` | HIT gaps ≥ 3 s, then 200. Task `Succeeded` |
| `demo-dlq.sh` | `QUEUE_RETRY_DELAYS=2,4,8`, `QUEUE_MAX_ATTEMPTS=4`; crawl `/fail/{k}`; heal + `POST /dead-letters/{id}/retry` | Dead-letter row (`attempt_count 4`, `"HTTP 500"`), task `DeadLettered`, about 15 s. After retry: `Succeeded`, `retried_at` set |
| `benchmark-tree.sh` | 4 workers; crawl `/tree/0` | 585 pages and 585 `Succeeded` tasks, 0 duplicates; pages/s printed |

### Files touched
- **Domain:**
  - `Crawling/CrawlTaskStatus.cs` (+`Skipped`), `CrawlTask.cs` (`DiscoveredByTaskId`, `Skip`, restart from a stale `InProgress`), `DeadLetterEntry.cs` (`RetriedAt`)
  - migration `<ts>_AddTaskLineageAndBenchmarkSite`
- **Application** `Crawling/`:
  - `CrawlTaskMessage`, `RetryPolicy`, `RetryOptions`, `TaskDecision`, `TaskDisposition`
  - ports `ICrawlStore`, `ITaskQueue`, `ILinkExtractor`, `ICrawlBudget`; `ISeenUrlStore` (+batch)
  - use cases `ProcessCrawlTask`, `StartCrawl`, `RetryDeadLetter`, `RecordDeadLetter`
- **Infrastructure:**
  - `Persistence/Stores/PostgresCrawlStore.cs`
  - `Crawling/{AngleSharpLinkExtractor,RedisCrawlBudget}.cs`, `RedisSeenUrlStore.cs`
  - `Messaging/{RabbitMqOptions,RabbitMqTopology,RabbitMqConnection,RabbitMqTaskQueue,RabbitMqQueueStats}.cs`
  - `AddCrawlQueue()`, attempt timeout from config
- **Worker:** `CrawlTaskConsumer.cs`, `DeadLetterRecorder.cs`, `Program.cs`
- **API:** `CrawlEndpoints.cs`, `HealthEndpoint.cs`, `Program.cs`
- **Contracts:** `StartCrawlRequest`, `StartCrawlResponse`, `HealthResponse` (+`QueueLength`)
- **TestSite:** `Program.cs`
- **Scripts:** `scripts/{lib,demo-worker-kill,demo-timeout,demo-429,demo-dlq,benchmark-tree}.sh`
- **Config:** `.env.example`, `docker-compose.yml`, `.github/workflows/ci.yml`
- **Docs:** new `docs/decisions/0012-queue-retries-fault-tolerance.md`; `CLAUDE.md`, `docs/CHANGELOG.md`, `README.md`, this plan

### New settings (`.env.example` + compose, optional with defaults)
| `.env` | → setting | Default | Used for |
|---|---|---|---|
| `QUEUE_MAX_ATTEMPTS` | `Queue__MaxAttempts` | `5` | attempts before dead-lettering |
| `QUEUE_RETRY_DELAYS` | `Queue__RetryDelays` | `5,30,120,600` (seconds) | delay tiers (one queue each) |
| `CRAWLER_ATTEMPT_TIMEOUT_SECONDS` | `Crawler__AttemptTimeoutSeconds` | `30` | per-attempt HTTP timeout |

Test-only variables (`SCRAPER_TEST_POSTGRES`, `SCRAPER_TEST_RABBITMQ`) are documented in README.

## Tests
Run them with `dotnet test`. With infrastructure: `docker compose up -d postgres redis rabbitmq` plus the
`SCRAPER_TEST_REDIS` / `SCRAPER_TEST_POSTGRES` / `SCRAPER_TEST_RABBITMQ` variables. In CI, every integration
test runs against service containers.

- **Step 2: unit (Domain)**
  - Additions to the `CrawlTask` tests (`Skipped`, restart from a stale `InProgress`, lineage).
- **Step 3: unit (`RetryPolicyTests`, message round trip)**
  - Every row of the decision table.
  - Attempt exhaustion at `MaxAttempts`.
  - Tier choice from backoff and from `Retry-After` (2 s → 5s, 90 s → 2m, 3600 s → 10m).
  - Other 4xx → dead-letter at once.
  - `CrawlTaskMessage` JSON round trip.
- **Step 4: unit (`ProcessCrawlTaskTests`, `StartCrawlTests`, `RetryDeadLetterTests`, with recording fakes)**
  - `Ack` only after `CompleteTaskAsync` commits; a store failure → `Retry`.
  - A duplicate (terminal) task → children re-published + `Ack`, no fetch.
  - Children filtered by scope, depth, seen and budget. Seen marked only after the commit.
  - 404 → page removed; robots → Skipped. Incremental validators. Redirect → target enqueued.
  - **`StartCrawl`:** unknown site 404, Browser 400, out-of-scope seed 400, publish failure → job Failed. `RetryDeadLetter`.
- **Step 5: integration, real Postgres (`PostgresCrawlStoreTests`, `[PostgresFact]`, one temporary migrated database per class)**
  - `CompleteTaskAsync` twice → 1 page, 1 fetch, no duplicate links or children (**idempotency**).
  - `TryStartTaskAsync`: terminal → false; stale `InProgress` → true.
  - `TryCompleteJobAsync` completes only when nothing is pending.
  - `RecordDeadLetter` twice → 1 row, updated. `RetryDeadLetter` resets the task and reopens the job.
- **Step 6: unit + integration**
  - `AngleSharpLinkExtractorTests` on fixture HTML (relative links, `<base>`, fragments, mailto/javascript, nofollow, text).
  - Real Redis: `RedisCrawlBudgetTests` (concurrent reservations never exceed the max) and seen-set batch operations.
- **Steps 7–9: integration, real RabbitMQ (`RabbitMqTopologyTests`, `RabbitMqConsumerTests`, `[RabbitFact]`, unique name prefix)**
  - Declaring the topology twice works.
  - A tier queue routes an expired message back (1 s tier).
  - **Unacked → redelivered after the connection closes** (`Redelivered=true`).
  - **Delivery limit** → `crawl.dead` with `x-death` reason `delivery_limit`.
  - `nack(requeue:false)` → DLQ (`rejected`).
  - **Graceful stop:** the in-flight message is acked before closing, with no redelivery.
  - Publisher confirms are awaited.
- **Step 11: CI.** `postgres:17-alpine` and `rabbitmq:4` service containers next to Redis, with the test variables set. No integration test is skipped in CI.
- **Step 13: E2E (scripts against `docker compose`).** `demo-worker-kill.sh`, `demo-timeout.sh`, `demo-429.sh` and `demo-dlq.sh` each print PASS with the results in the table above. `benchmark-tree.sh` crawls ≥ 500 pages with 0 duplicates.
- **All steps:** architecture tests green (Application has no RabbitMQ/Npgsql/AngleSharp references).

### Done criteria
- The 4 demo scripts each print **PASS** with their expected results (recorded in Changes and README).
- `benchmark-tree.sh` crawls ≥ 500 TestSite pages with 0 duplicate pages, fetches or tasks.
- All the tests pass locally and in CI (none skipped in CI). The format check and Release build are clean, and the architecture tests are green.
- `/health` shows `queueLength`. The heartbeat count is unchanged.
- ADR 0012 includes the topology and crash-recovery diagrams. CHANGELOG, README Manual testing and CLAUDE.md are updated.

## Decisions & trade-offs
(These are recorded in the new ADR 0012 "Queue topology, retries and fault tolerance".)
1. **Per-tier delay queues (TTL + DLX):** no head-of-line blocking. The cost is fixed delays.
2. **Ack after the DB commit and after confirmed publishes:** a crash never loses a task or its subtree.
3. **Prefetch 1:** a killed worker loses at most one in-flight task, and slow pages don't block others.
4. **Our own attempt count, with the delivery limit as a crash-loop guard.**
5. **Child rows in the parent's transaction act as an outbox,** re-published on redelivery.
6. **At-least-once delivery + idempotent writes** (P3 unique keys, guarded updates).
7. **A DLQ recorder writes `dead_letter_entries` for every dead-letter path** (worker give-ups and broker dead-letters).
8. **Seen set as a filter, Postgres as the arbiter;** URLs are marked seen after the commit.
9. **Graceful shutdown:** cancel the consumer, finish the in-flight task, 25 s deadline.
10. **Integration tests against CI service containers,** opt-in locally.

## Alternatives considered
1. **Per-message TTL on one delay queue:** head-of-line blocking (a long delay at the head blocks shorter ones). **Delayed-message plugin:** not in the official image, not replicated.
2. **Ack on receipt** (a crash loses the task). **Ack before publishing children** (a crash loses the subtree).
3. **A higher prefetch:** a killed worker takes several unacked tasks down with it, and one slow page holds the others back.
4. **Only `x-delivery-limit`:** delayed retries republish (resetting the count), and nack-requeue has no delay.
5. **A separate transactional-outbox table and relay:** more moving parts for the same guarantee here.
6. **Exactly-once delivery:** RabbitMQ can't provide it; idempotency is simpler and provable.
7. **Writing dead letters only in the worker's give-up path:** broker-side dead-letters would leave tasks `InProgress` forever.
8. **SADD before the insert:** a rolled-back transaction would leave URLs marked seen but never stored.
9. **Closing the connection on SIGTERM at once:** every stop would become a redelivery and a duplicate fetch.
10. **Testcontainers** (an extra dependency, as decided in P3/P4).

### Risks
- **A crash mid-fetch sends the site a second request** (rate-limited; the DB stays single-row).
- **Duplicate messages** (republished children, a retry published just before a crash). Handled by the guarded `TryStart` and unique keys.
- **A long DB/Redis outage** walks tasks through the tiers (about 12.5 min with the defaults) into the DLQ. `RetryDeadLetter` re-enqueues them.
- **`StartCrawl` publish failure** leaves the job `Failed` (503). No reconciler in P5.
- **Budget reservations leak** on rollback: safe (under-uses `MaxPages`), never an overshoot.
- **No jitter on queue-level delays.** Mitigated by the rate limiter and layer-1 jitter.
- **Single-node RabbitMQ:** quorum semantics without replication (documented).
- **Timing-based scripts** may be flaky on slow machines. They use `wait_until`, not fixed sleeps.
- **GTA5-Mods pagination depth** stays P6.
- **`POST /crawls` has no auth:** local-only until the auth milestone. The demos use TestSite only.

## Out of scope
Site parsers and content blocks/versions (P6), the Playwright fetcher for Newswire (P6), pagination-aware
depth, auth, dashboard pages, a dead-letter listing endpoint, and job cancellation.
