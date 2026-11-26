# Distributed Web Scraper

A distributed crawler for **GTA V vehicles and updates**, built on .NET 10. It crawls three sites:

- **GTACars.net**: mostly static HTML.
- **Rockstar Newswire**: JS-rendered, so it goes through Playwright.
- **GTA5-Mods.com**: paginated, 500+ pages.

Workers share a RabbitMQ queue and Redis state, results go into PostgreSQL, and a Blazor dashboard
shows them. This is Phase 1, Milestone 3 (P3): the domain model and database schema on top of the
skeleton (health endpoint, migrations, worker heartbeat). Crawling starts in the next milestone.

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
| Test site | http://localhost:8090 |

## Tests and checks

```bash
dotnet build
dotnet test                          # includes the architecture tests
dotnet format --verify-no-changes
```

Stop everything and delete the data with `docker compose down -v`.

## Manual testing

Step-by-step checks for what is implemented so far (current: **P3, domain model and persistence**).
Run them from the repository root, with a `.env` created from `.env.example`.

### 1. Automated checks
```bash
dotnet build
dotnet test                          # all test projects pass
dotnet format --verify-no-changes    # no output = OK
```

### 2. Start the stack (creates the tables)
```bash
docker compose up --build -d
docker compose ps -a                 # migrator: Exited (0); postgres/redis/rabbitmq: healthy; api: Up
docker compose logs migrator         # "Applying migration ... AddDomainModel", then "Migrations applied; exiting"
curl localhost:8080/health           # {"status":"healthy",...}
```
On a fresh database the migrator logs one `fail:` line for `__EFMigrationsHistory`. That's EF
checking for its history table before creating it, and it's harmless. If Postgres rejects the
password, an old volume still has a different one: run `docker compose down -v` and start again.

### 3. Inspect the database
```bash
docker compose exec postgres psql -U scraper -d scraper
```
```sql
\dt                                   -- 11 tables + __EFMigrationsHistory
\d pages                              -- unique (site_id, url), FK ... ON DELETE RESTRICT
\d document_versions                  -- unique (document_id, content_hash), index on vehicle_name
select id, fetch_mode, requests_per_second, max_depth, max_pages from sites;   -- 3 rows
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

### 8. Clean up
```bash
docker compose down -v     # removes the containers and test data (.env stays)
```

## Docs

- `docs/decisions/`: architecture decision records (ADRs)
- `docs/compliance/sites.md`: robots.txt, terms and rate limits per site
