# Changelog

Every notable change to the code, database schema, configuration, infrastructure or docs is noted
here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/):
changes go under **Unreleased** until a milestone is merged, grouped as Added / Changed / Fixed /
Removed / Database / Configuration.

## [Unreleased]

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

### Changed
- M3 closed out: its late items were folded into the M3 section below, and CLAUDE.md milestones
  now read M4 (P4) crawl core & politeness, M5+ frontier / consumers / parsers / stores.
- P3 plan notes that the upsert stores and depth semantics move to P5.
- `docs/compliance/sites.md`: Rockstar Newswire headless check **passed** (HTTP 200, 21 article links, no
  challenge) and a note that its robots.txt blocks named AI crawlers, which matters for Phase 2 RAG.

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
