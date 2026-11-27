# Changelog

Every notable change to the code, database schema, configuration, infrastructure or docs is noted
here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/):
changes go under **Unreleased** until a milestone is merged, grouped as Added / Changed / Fixed /
Removed / Database / Configuration.

## [Unreleased]

### Added
- P5 plan `docs/plans/2026-11-27-p5-queue-workers-fault-tolerance.md` (queue, workers and fault tolerance).
- Domain: `CrawlTaskStatus.Skipped`, `CrawlTask.DiscoveredByTaskId` (lineage), restart of a stale `InProgress` task after a
  crash, `Skip` / `ResetForRetry`; `CrawlJob.Reopen`; `DeadLetterEntry.RetriedAt`.
- `RetryPolicy` / `RetryOptions` / `TaskDecision`: classifies an attempt (succeed, page removed, skip, retry in a delay
  tier honouring Retry-After, or dead-letter after `MaxAttempts` / at once for permanent errors), and the
  `CrawlTaskMessage` contract for the work queue.
- Crawl ports `ICrawlStore`, `ITaskQueue`, `ILinkExtractor`, `ICrawlBudget`, `IFailureClassifier`, batch seen-set operations
  (`FilterUnseenAsync` before the insert, `MarkSeenAsync` after the commit) and `ISiteCatalog.FindByIdAsync`.
- Use cases: `ProcessCrawlTask` (claim → fetch → one-transaction completion → mark seen → publish children → ack;
  duplicates re-publish queued children), `StartCrawl`, `RetryDeadLetter`, `RecordDeadLetter`.
- `PostgresCrawlStore` (`ICrawlStore`): raw `INSERT … ON CONFLICT` upserts for pages (newest fetch wins, 404/410 set
  `removed_at`, a later success clears it), fetches (one per task), links, child tasks (the parent's outbox) and dead
  letters, plus guarded status updates. `[PostgresFact]` tests run in a throw-away migrated database when
  `SCRAPER_TEST_POSTGRES` is set; they prove that completing the same task twice leaves the same rows.
- `AngleSharpLinkExtractor` (`<a href>`, `<base href>`, skips mailto/javascript/tel, same-page and `rel=nofollow` links),
  `RedisCrawlBudget` (atomic `INCRBY` page budget per job), Redis seen-set batch operations (`SMISMEMBER` / `SADD`), and
  `FailureClassifier` (transient network/timeout/database/cache/broker errors vs permanent vs unknown).

### Database
- Migration `AddTaskLineageAndBenchmarkSite`: `crawl_tasks.discovered_by_task_id` (self FK, RESTRICT, indexed),
  `dead_letter_entries.retried_at`, and `testsite` raised to 5 req/s and 1000 pages for the 585-page benchmark tree.

### Changed
- README reorganized: table of contents; one place each for setup (with a settings table), commands, tests and
  troubleshooting (previously repeated across "Run", "Tests and checks" and "Manual testing"); target sites as a table;
  manual-testing steps now refer to those sections instead of repeating them.
- M4 closed out: its items were folded into the M4 section below; CLAUDE.md milestones updated.

## [M4 / P4] Crawl core and politeness: 2026-11-27 (PR #2)

### Added
- P4 plan `docs/plans/2026-11-27-p4-crawl-core-politeness.md` (crawl core and politeness).
- `RobotsTxt`: RFC 9309 robots.txt parser (group selection by product token, then `*`, then allow-all;
  longest match wins, Allow wins ties, `*` / `$` patterns, 500 KiB cap; 2xx parse, 4xx allow all,
  5xx disallow all), with tests against the captured Rockstar, GTACars and GTA5-Mods robots.txt files.
- `NormalizedUrl.Scheme` / `Host` / `Authority` / `Path` / `PathAndQuery`, and `SiteConfig.IsInScope(url)`
  (same scheme and authority as the base URL, path under an allowed prefix by whole segments).
- Application crawl ports (`IRobotsPolicy`, `ISeenUrlStore`, `IRateLimiter`, `IFetcher`, `ISiteCatalog`) and the
  `PoliteFetcher` use case: known site → in scope → robots.txt → fetch; blocked URLs never reach the network,
  redirects are reported (normalized) but not followed.
- `RedisSeenUrlStore`: one Redis set per crawl job (`crawl:{jobId}:seen`, 7-day expiry); `SADD` is the atomic
  check-and-mark shared by all workers. `[RedisFact]` tests run against a real Redis when `SCRAPER_TEST_REDIS` is set.
- `RedisRateLimiter`: per-site token bucket in an atomic Lua script (`token-bucket.lua`, capacity 1, Redis `TIME`
  as the clock) shared by all workers; tested with 8 independent connections staying within 5 req/s.
- `RedisRobotsPolicy`: robots.txt checks with a shared Redis cache per origin (24 h after 2xx/4xx, 5 min after
  5xx or network errors), fetched through `IFetcher` so it's rate-limited, following up to 5 same-site redirects.
  `CrawlerOptions` reads `Crawler__UserAgent` and derives the robots.txt product token.
- `HttpFetcher` and the `AddCrawler()` registration: the "crawler" HttpClient retries 429/503/timeouts/connection
  errors (3 retries, exponential backoff with jitter, honors Retry-After up to 60 s), takes a shared rate-limit
  token on **every attempt** (`RateLimitingHandler` sits inside the retry pipeline), has a 30 s per-attempt and
  5 min total timeout, sends `Crawler__UserAgent` and conditional headers, never follows redirects, refuses
  hosts that aren't configured sites, and caps responses at 10 MB. `SiteCatalog` caches the `sites` table for 5 min.
- TestSite: `/robots.txt` (disallows `/private/`), `/pages/{n}` (each links to the next), `/status/{code}`
  (429/503 send `Retry-After: 2`), and a `HIT <UTC time> <path> <user-agent>` log line per request.
- Worker `FetchProbeService` (off unless `Crawler__ProbeUrl` is set) fetches one URL in a loop through the full
  polite pipeline, and `tools/check-rate.sh` checks TestSite's request log: 8 workers → 0.997 req/s at a 1 req/s limit.
- ADR 0011 "Politeness pipeline" (shared Lua token bucket, rate limit on every attempt, no automatic redirects,
  RFC 9309 robots semantics, per-job seen set).

### Changed
- M3 closed out: its late items were folded into the M3 section below, and CLAUDE.md milestones
  now read M4 (P4) crawl core & politeness, M5+ frontier / consumers / parsers / stores.
- P3 plan notes that the upsert stores and depth semantics move to P5.
- README: intro names M4 (P4); Manual testing adds the Redis-backed test run and the 8-worker rate-limit proof.
- CLAUDE.md: layout lists the P4 crawl components, ADRs 0001–0011 and `tools/check-rate.sh`.
- `docs/compliance/sites.md`: Rockstar Newswire headless check **passed** (HTTP 200, 21 article links, no
  challenge) and a note that its robots.txt blocks named AI crawlers, which matters for Phase 2 RAG.

### Database
- Migration `AddTestSite`: seeds a 4th site, `testsite` (`http://testsite:8080/`, Http, 1 req/s, depth 3, 100 pages),
  used for the shared rate-limit proof.

### Configuration
- New optional setting `CRAWLER_PROBE_URL` → `Crawler__ProbeUrl` (worker only; empty = probe off) in `.env.example`
  and `docker-compose.yml`.
- CI `build-test` job runs a `redis:7-alpine` service container and sets `SCRAPER_TEST_REDIS`, so the
  Redis-backed tests run on every push (locally they're skipped unless the variable is set).

## [M3 / P3] Domain model and persistence: 2026-11-27 (PR #1)

### Added
- Domain value objects: `NormalizedUrl` (canonical http(s) URLs) and `ContentHash` (SHA-256 over
  canonical content via `DocumentContent`, which excludes metrics, links, raw HTML and timestamps, per ADR 0009).
- Domain entities: `SiteConfig`, `CrawlJob`, `CrawlTask`, `DeadLetterEntry`, `Page`, `PageFetch`,
  `Link`, `Document`, `DocumentVersion`, `ContentBlock` / `TableBlock`, `MetricsSnapshot`, and the
  site-field records `GtaCarsFields`, `NewswireFields`, `Gta5ModsFields`.
- EF Core configurations for every entity, with snake_case naming (`EFCore.NamingConventions` 10.0.1).
- Tests: 53 domain unit tests (URL normalization, hashing) and 14 model tests (migration matches
  the model, unique natural keys, no cascade deletes, site fields JSON round trip).
- ADR 0010 "Content storage model" and the P3 plan in `docs/plans/`.
- `docs/CHANGELOG.md` (this file), and a CLAUDE.md rule to update it with every change.
- CLAUDE.md "Plan changes rule": every change made while implementing a plan is explained in that plan's `## Changes` section.
- README "Manual testing" section (start the stack, inspect the schema, check idempotent upserts, RESTRICT deletes and migration re-runs), and a CLAUDE.md rule to keep it current.

### Database
- Migration `AddDomainModel`: 11 tables (`sites`, `crawl_jobs`, `crawl_tasks`, `dead_letter_entries`,
  `pages`, `page_fetches`, `links`, `documents`, `document_versions`, `content_blocks`,
  `metrics_snapshots`), unique natural-key indexes for idempotent upserts, and every foreign key `ON DELETE RESTRICT`.
- Seeded sites: `gtacars` (Http, 0.5 req/s), `newswire` (Browser, 0.2 req/s), `gta5mods` (Http, 0.5 req/s).

### Changed
- CLAUDE.md: milestones (M3 = P3), layout, ADR range, a new persistence-rules section.
- `docs/compliance/sites.md`: seeded rate limits noted next to each site.
- README intro now names the current milestone (M3 / P3) instead of M2.

### Configuration
- No new settings. A local `.env` (git-ignored) can be created from `.env.example`.
- `docker-compose.yml` publishes PostgreSQL on host port **5434** and Redis on **6381**, so they can be
  inspected from the host without clashing with a Postgres/Redis already running there on 5432/6379.
  Container-to-container connections are unchanged (`postgres:5432`, `redis:6379`). README URLs updated.

## [M2] Skeleton: 2026-11-23

### Added
- .NET 10 solution (`.slnx`) with Domain, Application, Infrastructure, Infrastructure.Browser,
  Contracts, Api, Worker, Web and a local test site; central package management.
- `/health` endpoint (Postgres, Redis, RabbitMQ, active workers), the `--migrate-only` migrator flag
  and the worker heartbeat.
- Architecture tests enforcing the dependency rule.
- Dockerfiles and `docker-compose.yml` (Postgres 17, Redis 7, RabbitMQ 4, migrator, api, worker, web, testsite).
- GitHub Actions CI: format check, build, tests, image builds.
- README, CLAUDE.md, ADRs 0001–0009 and site compliance notes.

### Database
- Empty `InitialCreate` migration.

## [M1] Planning: 2026-11-23

### Added
- Theme (GTA V vehicles and updates), target sites, stack and solution layout decisions.
