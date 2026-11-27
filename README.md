# Distributed Web Scraper

A distributed crawler for **GTA V vehicles and updates**, built on .NET 10. It crawls three sites:

- **GTACars.net**: mostly static HTML.
- **Rockstar Newswire**: JS-rendered, so it goes through Playwright.
- **GTA5-Mods.com**: paginated, 500+ pages.

Workers share a RabbitMQ queue and Redis state, results go into PostgreSQL, and a Blazor dashboard
shows them. This is Phase 1, Milestone 4 (P4): a polite single-URL fetch pipeline (robots.txt, a rate
limit shared by all workers, retries) on top of the domain model and schema. Queue consumers and
parsers come next.

## Prerequisites

- Docker with Compose v2
- .NET SDK 10.0.302 or a later 10.0 feature band, to build and test outside Docker (see `global.json`)

## Run

```bash
cp .env.example .env      # then replace the placeholder values
docker compose up --build
```

The `migrator` service applies database migrations first. `api` and `worker` start only after it exits successfully.

## Scale the workers

```bash
docker compose up -d --scale worker=4
curl localhost:8080/health   # "activeWorkers": 4 within a few seconds
```

## URLs

| Service | URL |
|---|---|
| API health | http://localhost:8080/health |
| API reference (Scalar) | http://localhost:8080/scalar |
| OpenAPI document | http://localhost:8080/openapi/v1.json |
| Web dashboard | http://localhost:8081 |
| RabbitMQ management | http://localhost:15672 (credentials from `.env`) |
| PostgreSQL (from the host) | `localhost:5434` (user, password and database from `.env`) |
| Redis (from the host) | `localhost:6381` |
| Test site | http://localhost:8090 |

## Tests and checks

```bash
dotnet build
dotnet test                          # includes the architecture tests
dotnet format --verify-no-changes
```

Redis-backed tests (rate limiter, seen set, robots cache) are skipped unless `SCRAPER_TEST_REDIS` is set.
CI sets it for its Redis service container; locally:

```bash
docker compose up -d redis
SCRAPER_TEST_REDIS=localhost:6381 dotnet test
```

Stop everything and delete the data with `docker compose down -v`.

## Manual testing

Step-by-step checks for what is implemented so far (current: **P4, crawl core and politeness**).
Run them from the repository root, with a `.env` created from `.env.example`.

### 1. Automated checks
```bash
dotnet build
dotnet test                          # all test projects pass (Redis-backed ones skipped)
SCRAPER_TEST_REDIS=localhost:6381 dotnet test   # after `docker compose up -d redis`: none skipped
dotnet format --verify-no-changes    # no output = OK
```

### 2. Start the stack (creates the tables)
```bash
docker compose up --build -d
docker compose ps -a                 # migrator: Exited (0); postgres/redis/rabbitmq: healthy; api: Up
docker compose logs migrator         # "Applying migration ... AddTestSite", then "Migrations applied; exiting"
curl localhost:8080/health           # {"status":"healthy",...}
```
On a fresh database the migrator logs one `fail:` line for `__EFMigrationsHistory`. That's EF
checking for its history table before creating it, and it's harmless. If Postgres rejects the
password, an old volume still has a different one: run `docker compose down -v` and start again.

### 3. Inspect the database
```bash
docker compose exec postgres psql -U scraper -d scraper
# or, with psql or a GUI client on the host: localhost:5434 (credentials from .env)
```
```sql
\dt                                   -- 11 tables + __EFMigrationsHistory
\d pages                              -- unique (site_id, url), FK ... ON DELETE RESTRICT
\d document_versions                  -- unique (document_id, content_hash), index on vehicle_name
select id, fetch_mode, requests_per_second, max_depth, max_pages from sites;   -- 4 rows (incl. testsite)
```

### 4. Idempotency: the same write twice adds one row
```sql
insert into pages (id, site_id, url, first_seen_at)
values (gen_random_uuid(), 'gtacars', 'https://gtacars.net/gta5/adder', now())
on conflict (site_id, url) do nothing;
-- run it again: INSERT 0 0
select count(*) from pages;           -- 1
```

### 5. Unchanged content creates no new version
```sql
insert into documents (id, page_id, kind, first_seen_at)
select gen_random_uuid(), id, 'Vehicle', now() from pages limit 1;

insert into document_versions (id, document_id, content_hash, captured_at, title, site_fields)
select gen_random_uuid(), id, repeat('a', 64), now(), 'Adder', '{"site":"gtacars"}' from documents
on conflict (document_id, content_hash) do nothing;
-- run it again: INSERT 0 0
```

### 6. Pages are marked removed, never deleted
```sql
update pages set removed_at = now();                 -- how "removed" is recorded
delete from sites where id = 'gtacars';              -- ERROR: still referenced (RESTRICT)
delete from pages;                                   -- ERROR: referenced by documents
```

### 7. Re-running the migrations is a no-op
```bash
docker compose up -d migrator
docker compose logs --tail 5 migrator   # "Migrations applied", no new "Applying migration"
```

### 8. Shared rate limit: 8 workers, one site budget
Every worker fetches the same TestSite page in a loop through the full pipeline (scope → robots.txt →
shared Redis token bucket → HTTP). TestSite allows 1 req/s and logs a timestamp per request.
```bash
CRAWLER_PROBE_URL=http://testsite:8080/pages/1 docker compose up --build -d --scale worker=8
docker compose logs -f worker | grep Probe   # each worker: "Probe Fetched status=200 ..." (Ctrl+C to stop)
tools/check-rate.sh                          # after a minute or two
```
Expected (numbers vary slightly):
```
requests:            123
average rate:        0.997 req/s (limit 1)
smallest gap:        0.973 s (expected about 1.000 s)
RESULT:              PASS (all workers together stayed within the limit)
```
Also check:
```bash
docker compose logs --no-log-prefix testsite | grep -c ' /robots.txt '   # 1: cached in Redis for all workers
docker compose up -d --scale worker=2 worker                            # without the variable: probe off
```

### 9. Clean up
```bash
docker compose down -v     # removes the containers and test data (.env stays)
```

## Docs

- `docs/decisions/`: architecture decision records (ADRs)
- `docs/compliance/sites.md`: robots.txt, terms and rate limits per site
