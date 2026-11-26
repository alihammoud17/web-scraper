# CLAUDE.md: context for future sessions

## Project
Individual, graded **Distributed Web Scraper** (Phase 1). Every decision must be defensible in a
report and a narrated video: keep it simple, explain choices, and add nothing that wasn't asked for.

Theme: **GTA V vehicles and updates.** The **vehicle name is the shared key** across all three sites
(Phase 2 joins on it).

| Site | Requirement | Target | Notes |
|---|---|---|---|
| A | Mostly static HTML | GTACars.net | Server-rendered (Nuxt). Class, price, top speed, lap time, plus stats **tables** (extra content type) |
| B | JS-rendered | Rockstar Newswire (`rockstargames.com/newswire`) | SPA: raw HTML has no articles until JS runs → Playwright |
| C | Paginated, 500+ pages | GTA5-Mods.com | `/vehicles/N` listings (884+ pages) plus mod detail pages |

Compliance notes per site: `docs/compliance/sites.md` (never call GTACars `/api/`, never follow
GTA5-Mods download links, never bypass a bot challenge).

Stack: .NET 10 / C# 14, ASP.NET Core minimal API, Worker Service, Blazor Web App (Interactive
Server), RabbitMQ 4 (quorum queues + DLX) via raw `RabbitMQ.Client` 7 (no MassTransit), Redis,
PostgreSQL 17 + EF Core (Npgsql), Microsoft.Playwright (worker only), AngleSharp, xUnit (plain
asserts, **no FluentAssertions**), GitHub Actions. Auth (later): API-issued JWT with `admin` /
`viewer` roles, users seeded from env vars.

## Dependency rule (enforced by tests/Scraper.Application.Tests/ArchitectureTests.cs)
`Domain ← Application ← Infrastructure / Infrastructure.Browser / hosts`
- Domain and Application never reference EF Core, Npgsql, Redis, RabbitMQ, Playwright,
  AngleSharp or ASP.NET Core.
- Only the Worker references `Scraper.Infrastructure.Browser` (keeps Playwright out of the API image).
- `Scraper.Contracts` = DTOs only, no dependencies, shared by Api and Web. Web never references Application.

## Layout
```
src/Scraper.Domain                  entities, value objects (NormalizedUrl, ContentHash, pages, documents, versions)
src/Scraper.Application             use cases, ports (empty until P4)
src/Scraper.Infrastructure          EF Core (ScraperDbContext, Configurations/, migrations, snake_case), Redis, RabbitMQ; AddInfrastructure()
src/Scraper.Infrastructure.Browser  Playwright (empty in M2)
src/Scraper.Contracts               API DTOs (HealthResponse)
src/Scraper.Api                     minimal API: /health, --migrate-only, OpenAPI + Scalar
src/Scraper.Worker                  BackgroundServices (HeartbeatService)
src/Scraper.Web                     Blazor dashboard; calls the API via named HttpClient "Api"
tools/Scraper.TestSite              local site for crawler tests
tests/*                             xUnit test projects
docs/decisions                      ADRs 0001–0010
docs/plans                          approved step plans (saved with /save-plan)
docs/compliance/sites.md            robots.txt / terms / rate limits per site
```
Package versions live only in `Directory.Packages.props` (central package management).
Framework-tied packages stay on 10.0.x. The Worker Dockerfile's Playwright image tag must equal
the `Microsoft.Playwright` version.

## Configuration rules
- **No secrets in the repo.** Settings come from environment variables only; nothing secret in `appsettings*.json`.
- **Every new setting goes in `.env.example`** (safe placeholder) and in `docker-compose.yml`
  (`${VAR:?missing}` for secrets). `.env` is git-ignored.
- Current settings: `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `RabbitMQ__Host`,
  `RabbitMQ__Username`, `RabbitMQ__Password`, `Jwt__SigningKey`, `Seed__AdminPassword`,
  `Seed__ViewerPassword`, `Crawler__UserAgent`, `Api__BaseUrl`.

## Phase 2 requirement (design for it now)
Phase 2 adds RAG over the crawled content. **Store structured blocks (headings, paragraphs,
lists, tables, code), links and content versions; never only flattened text.**

## Volatile-fields rule
Download counts, likes and view counts change constantly. Store them as **separate metrics
snapshots** and keep them **out of the content hash**, so re-crawls don't create fake versions
(ADR 0009).

## Persistence rules (ADR 0010)
- Every worker write is an `INSERT … ON CONFLICT` on a natural key; never check-then-insert.
- Never delete pages or content: set `pages.removed_at`. All FKs are `ON DELETE RESTRICT`.
- Site-specific fields go in `document_versions.site_fields` (jsonb) via the typed `*Fields` records only.

## Milestones
- [x] M1: Planning and decisions (theme, sites, stack, layout)
- [x] M2: Skeleton: solution, Docker Compose, CI, docs, health + heartbeat
- [ ] M3 (P3): Domain model & persistence: entities, value objects, EF Core mapping, first real migration (plan: `docs/plans/2026-11-26-p3-domain-model-persistence.md`)
- [ ] M4+: Crawler: fetching, parsing, robots.txt, rate limiting, seen set, queue consumers
- [ ] M6: Verify Rockstar Newswire renders in headless Playwright without a bot challenge
- [ ] Later: auth (JWT roles), dashboard pages, full-text search

## Commands
`dotnet build` · `dotnet test` · `dotnet format --verify-no-changes` ·
`docker compose up --build` · `docker compose up -d --scale worker=4` · `docker compose down -v`
Migrations: `dotnet ef migrations add <Name> --project src/Scraper.Infrastructure --startup-project src/Scraper.Infrastructure --output-dir Persistence/Migrations`
