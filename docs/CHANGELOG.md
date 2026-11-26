# Changelog

Every notable change to the code, database schema, configuration, infrastructure or docs is noted
here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/):
changes go under **Unreleased** until a milestone is merged, grouped as Added / Changed / Fixed /
Removed / Database / Configuration.

## [Unreleased]

### Added
- `docs/CHANGELOG.md` (this file), and a CLAUDE.md rule to update it with every change.
- CLAUDE.md "Plan changes rule": every change made while implementing a plan is explained in that plan's `## Changes` section.
- README "Manual testing" section (start the stack, inspect the schema, check idempotent upserts, RESTRICT deletes and migration re-runs), and a CLAUDE.md rule to keep it current.

### Changed
- README intro now names the current milestone (M3 / P3) instead of M2.

## [M3 / P3] Domain model and persistence: 2026-11-26 (branch `p3-domain-model`)

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

### Database
- Migration `AddDomainModel`: 11 tables (`sites`, `crawl_jobs`, `crawl_tasks`, `dead_letter_entries`,
  `pages`, `page_fetches`, `links`, `documents`, `document_versions`, `content_blocks`,
  `metrics_snapshots`), unique natural-key indexes for idempotent upserts, and every foreign key `ON DELETE RESTRICT`.
- Seeded sites: `gtacars` (Http, 0.5 req/s), `newswire` (Browser, 0.2 req/s), `gta5mods` (Http, 0.5 req/s).

### Changed
- CLAUDE.md: milestones (M3 = P3), layout, ADR range, a new persistence-rules section.
- `docs/compliance/sites.md`: seeded rate limits noted next to each site.

### Configuration
- No new settings. A local `.env` (git-ignored) can be created from `.env.example`.

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
