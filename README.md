# Distributed Web Scraper

A distributed crawler for **GTA V vehicles and updates**, built on .NET 10. Workers share a RabbitMQ
queue and Redis state, results go into PostgreSQL, and a Blazor dashboard shows them.

**Status:** Phase 1, Milestone 5 (P5): stateless workers consume a RabbitMQ work queue, crawl politely
(robots.txt, a rate limit shared by all workers, retries), store pages, fetches and links idempotently, and
survive killed workers, timeouts, 429s and failing URLs. Site parsers and the Playwright fetcher come next.

## Contents

1. [Target sites](#target-sites)
2. [Prerequisites](#prerequisites)
3. [Getting started](#getting-started)
4. [Services and URLs](#services-and-urls)
5. [Everyday commands](#everyday-commands)
6. [Tests](#tests)
7. [Manual testing](#manual-testing)
8. [Troubleshooting](#troubleshooting)
9. [Documentation](#documentation)

## Target sites

The **vehicle name** is the shared key across the three sites.

| Site | Kind | How it's fetched |
|---|---|---|
| **GTACars.net** | Mostly static HTML | Plain HTTP |
| **Rockstar Newswire** | JS-rendered single-page app | Headless browser (Playwright) |
| **GTA5-Mods.com** | Paginated, 500+ listing pages | Plain HTTP |

Robots.txt rules, terms and rate limits per site are in [`docs/compliance/sites.md`](docs/compliance/sites.md).

## Prerequisites

- Docker with Compose v2.
- .NET SDK 10.0.302 or a later 10.0 feature band (see `global.json`), only to build and test outside Docker.

## Getting started

1. Create your settings file and replace every placeholder (`.env` is git-ignored):
   ```bash
   cp .env.example .env
   ```
   | Variable | Purpose |
   |---|---|
   | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Database |
   | `RABBITMQ_USER`, `RABBITMQ_PASSWORD` | Queue |
   | `JWT_SIGNING_KEY` (32+ characters), `SEED_ADMIN_PASSWORD`, `SEED_VIEWER_PASSWORD` | Auth (later milestone) |
   | `CRAWLER_USER_AGENT` | The bot's name plus a real contact address; every site sees it |
   | `CRAWLER_PROBE_URL` | Optional; leave empty (see [Manual testing, step 9](#9-shared-rate-limit-8-workers-one-site-budget)) |
   | `QUEUE_MAX_ATTEMPTS`, `QUEUE_RETRY_DELAYS`, `CRAWLER_ATTEMPT_TIMEOUT_SECONDS` | Optional retry tuning (defaults 5 attempts; 5 s, 30 s, 2 min, 10 min; 30 s). The demo scripts set short values themselves |

2. Start everything:
   ```bash
   docker compose up --build -d
   ```
   The one-shot `migrator` service applies the database migrations first (tables plus the seeded
   `sites` rows). `api` and `worker` start only after it exits successfully.

3. Check that it's up:
   ```bash
   docker compose ps -a        # migrator: Exited (0); postgres, redis, rabbitmq: healthy; api, worker: Up
   curl localhost:8080/health  # {"status":"healthy",...}
   ```

## Services and URLs

| Service | URL |
|---|---|
| API health | http://localhost:8080/health |
| API reference (Scalar) | http://localhost:8080/scalar |
| OpenAPI document | http://localhost:8080/openapi/v1.json |
| Web dashboard | http://localhost:8081 |
| RabbitMQ management | http://localhost:15672 (credentials from `.env`) |
| PostgreSQL (from the host) | `localhost:5434` (database, user and password from `.env`) |
| Redis (from the host) | `localhost:6381` |
| RabbitMQ AMQP (from the host) | `localhost:5673` (credentials from `.env`) |
| Test site | http://localhost:8090 |

Inside the compose network the services keep their default ports (`postgres:5432`, `redis:6379`, `rabbitmq:5672`).

## Everyday commands

```bash
docker compose up -d --scale worker=4      # run 4 workers; /health shows "activeWorkers": 4 within seconds
docker compose logs -f worker              # follow the workers' logs
curl -X POST localhost:8080/crawls -H 'content-type: application/json' \
     -d '{"siteId":"testsite","seeds":["http://testsite:8080/pages/1"]}'   # start a crawl → 202 {"jobId":…}
curl -X POST localhost:8080/dead-letters/<taskId>/retry                    # re-queue a dead-lettered task
docker compose exec postgres psql -U scraper -d scraper   # SQL shell
docker compose down                        # stop, keep the data
docker compose down -v                     # stop and delete all data (.env stays)
```

## Tests

```bash
dotnet build
dotnet test                          # unit, model and architecture tests
dotnet format --verify-no-changes    # no output = OK
```

Integration tests against real infrastructure are skipped unless their variable is set; CI sets all three for
its service containers:

| Variable | Tests | Local value (after `docker compose up -d postgres redis rabbitmq`) |
|---|---|---|
| `SCRAPER_TEST_REDIS` | rate limiter, seen set, page budget, robots.txt cache | `localhost:6381` |
| `SCRAPER_TEST_POSTGRES` | crawl store (idempotent upserts); each test class uses a throw-away database | `Host=localhost;Port=5434;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>;Database=<POSTGRES_DB>` |
| `SCRAPER_TEST_RABBITMQ` | topology, delay tiers, redelivery, delivery limit, graceful stop | `amqp://<RABBITMQ_USER>:<RABBITMQ_PASSWORD>@localhost:5673/` |

CI (GitHub Actions) runs the format check, build and all tests on every push, and builds the Docker images.

## Manual testing

Step-by-step checks for what is implemented so far (current: **P5, queue, workers and fault tolerance**).
Start with a running stack ([Getting started](#getting-started)) and the [automated tests](#tests)
passing. Steps 2–5 use the SQL shell from [Everyday commands](#everyday-commands). Steps 7–8 are scripts that
print each check and a final `RESULT: PASS` or `FAIL`.

### 1. Migrations applied
```bash
docker compose logs migrator   # "Applying migration ..." up to AddTaskLineageAndBenchmarkSite, then "Migrations applied; exiting"
```

### 2. Inspect the database
```sql
\dt                                   -- 11 tables + __EFMigrationsHistory
\d pages                              -- unique (site_id, url), FK ... ON DELETE RESTRICT
\d document_versions                  -- unique (document_id, content_hash), index on vehicle_name
select id, fetch_mode, requests_per_second, max_depth, max_pages from sites;   -- 4 rows (testsite: 5 req/s, 1000 pages)
```
Crawls (step 7) fill `crawl_jobs`, `crawl_tasks`, `pages`, `page_fetches` and `links`. Content blocks and versions come with the parsers (P6).

### 3. Idempotency: the same write twice adds one row
```sql
insert into pages (id, site_id, url, first_seen_at)
values (gen_random_uuid(), 'gtacars', 'https://gtacars.net/gta5/adder', now())
on conflict (site_id, url) do nothing;
-- run it again: INSERT 0 0
select count(*) from pages;           -- 1
```

### 4. Unchanged content creates no new version
```sql
insert into documents (id, page_id, kind, first_seen_at)
select gen_random_uuid(), id, 'Vehicle', now() from pages limit 1;

insert into document_versions (id, document_id, content_hash, captured_at, title, site_fields)
select gen_random_uuid(), id, repeat('a', 64), now(), 'Adder', '{"site":"gtacars"}' from documents
on conflict (document_id, content_hash) do nothing;
-- run it again: INSERT 0 0
```

### 5. Pages are marked removed, never deleted
```sql
update pages set removed_at = now();                 -- how "removed" is recorded
delete from sites where id = 'gtacars';              -- ERROR: still referenced (RESTRICT)
delete from pages;                                   -- ERROR: referenced by documents
```

### 6. Re-running the migrations is a no-op
```bash
docker compose up -d migrator
docker compose logs --tail 5 migrator   # "Migrations applied", no new "Applying migration"
```

### 7. Crawl through the queue
```bash
curl -X POST localhost:8080/crawls -H 'content-type: application/json' \
     -d '{"siteId":"testsite","seeds":["http://testsite:8080/pages/1"]}'   # 202 {"jobId":"…","seedCount":1}
```
```sql
select depth, status, attempt_count, url from crawl_tasks where crawl_job_id = '<jobId>' order by depth;
-- pages/1 … pages/4 (depth 0–3, the site's depth limit), all Succeeded with 1 attempt
select status from crawl_jobs where id = '<jobId>';   -- Completed
```
`curl localhost:8080/health` shows `"queueLength"`, the tasks waiting in `crawl.tasks`. The RabbitMQ management UI
(http://localhost:15672) shows the topology: `crawl.tasks`, `crawl.retry.<tier>` and `crawl.dead`.

### 8. Fault-tolerance demos
Each script sets up its own workers (and restores 2 workers with default settings at the end):

| Script | Scenario | Expected (RESULT: PASS) |
|---|---|---|
| `scripts/demo-worker-kill.sh` | `docker kill` a worker in the middle of a task, then `docker stop` another | The killed task is redelivered (`redelivered=True`) and finished by another worker: 13/13 pages, 13 fetch rows, 0 duplicates. The stopped worker finishes and acks its task, which runs exactly once |
| `scripts/demo-timeout.sh` | A URL that hangs twice, and one that always hangs (2 s timeout) | The first succeeds within one task attempt (3 requests); the second is retried through the queue and dead-lettered after 2 attempts (8 requests), reason names the timeout |
| `scripts/demo-429.sh` | 429 with `Retry-After: 3`, twice | Requests about 3 s apart (e.g. 0.0, 3.0, 6.1 s), then 200; task `Succeeded` |
| `scripts/demo-dlq.sh` | A URL that always returns 500 (tiers 2/4/8 s, 4 attempts), then fixed | Dead-lettered in about 15 s with `gave up after 4 attempts: HTTP 500`; after `POST /dead-letters/{taskId}/retry` it succeeds |
| `scripts/benchmark-tree.sh` | 4 workers crawl the 585-page TestSite tree | 585/585 tasks, 0 duplicates, about 4.8 pages/s (TestSite allows 5 req/s) in about 2 minutes |

Watch it live: `docker compose logs -f worker` (Processing / done / will retry / dead-lettered lines).

### 9. Shared rate limit: 8 workers, one site budget
Every worker fetches the same TestSite page in a loop through the full pipeline (scope → robots.txt →
shared Redis token bucket → HTTP). TestSite allows 5 req/s and logs a timestamp per request.
```bash
docker compose up -d --force-recreate testsite   # fresh request log
CRAWLER_PROBE_URL=http://testsite:8080/pages/1 docker compose up -d --scale worker=8
docker compose logs -f worker | grep Probe       # each worker: "Probe Fetched status=200 ..." (Ctrl+C to stop)
tools/check-rate.sh                              # after a minute or two (counts only the crawler's requests)
```
Expected (numbers vary slightly):
```
max in any  1 s:     6 (allowed 6)
max in any 10 s:     49 (allowed 51)
average rate:        4.88 req/s (limit 5)
smallest gap:        0.188 s (nominal 0.200 s; a few ms of network jitter is normal)
RESULT:              PASS (all workers together stayed within the limit)
```
A token bucket with capacity 1 allows at most ⌊rate × W⌋ + 1 requests in any W-second window; that's what the
script checks. Then:
```bash
docker compose logs --no-log-prefix testsite | grep -c ' /robots.txt '   # at most 1: cached in Redis for 24 h
docker compose up -d --scale worker=2 worker                            # without the variable: probe off
```

When you're done, clean up with `docker compose down -v`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `docker compose up` stops with "required variable … is missing a value" | `.env` is missing or incomplete: see [Getting started](#getting-started). |
| The migrator logs one `fail:` line about `__EFMigrationsHistory` | Harmless: on an empty database EF looks for its history table before creating it. |
| Postgres or RabbitMQ rejects the password | Their data volume was created with an older password. Run `docker compose down -v` (deletes the data) and start again. |
| A demo script prints FAIL | Each check line shows what it got and expected. Make sure the images are current (`docker compose up --build -d`) and look at `docker compose logs worker`. |
| A port is already in use | Postgres, Redis and RabbitMQ (AMQP) are published on 5434, 6381 and 5673 to avoid local instances on 5432/6379/5672. Change the mapping in `docker-compose.yml` if needed. |

## Documentation

| Where | What |
|---|---|
| [`docs/decisions/`](docs/decisions/) | Architecture decision records (ADRs 0001–0012) |
| [`docs/plans/`](docs/plans/) | Approved plan for each step, with what changed during implementation |
| [`docs/compliance/sites.md`](docs/compliance/sites.md) | robots.txt, terms and rate limits per site |
| [`docs/CHANGELOG.md`](docs/CHANGELOG.md) | Every notable change, newest first |
| [`CLAUDE.md`](CLAUDE.md) | Project rules, layout and conventions |
