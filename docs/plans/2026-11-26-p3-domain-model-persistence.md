# P3: Domain model and persistence

- **Date:** 2026-11-26
- **Branch:** main
- **Goal:** Design the Domain entities and value objects and the EF Core persistence layer, and add the first
  real migration with idempotency constraints. This step has no crawling logic and no store/upsert code
  (that comes in P4).
- **Status:** done (changes not committed yet)
- **Approval:** approved in plan mode

## Context
M2 left `Scraper.Domain` empty, `ScraperDbContext` with no entities, and an empty `InitialCreate`
migration. Every later step (fetching, parsing, queue consumers, dashboard, Phase 2 RAG) needs a
stable schema. P3 adds the schema and enforces idempotency with natural-key unique constraints. It
documents the upsert for each table, and P4 writes the store classes next to the consumers that
call them.

## Steps
- [x] 1. `docs: align CLAUDE.md milestones and stale comments with P3`
- [x] 2. `feat(domain): add NormalizedUrl value object with tests`
- [x] 3. `feat(domain): add ContentHash and canonical content hashing with tests`
- [x] 4. `feat(domain): add site, crawl, page and content entities`
- [x] 5. `build: add EFCore.NamingConventions for snake_case tables`
- [x] 6. `feat(infrastructure): map domain model with EF Core configurations`
- [x] 7. `feat(infrastructure): add AddDomainModel migration with seeded sites`
- [x] 8. `test(infrastructure): assert unique keys, restrict deletes and no pending model changes`
- [x] 9. `docs: add ADR 0010 content storage model; update CLAUDE.md and sites.md`

Each step is one small commit.

### Drift fixes (steps 1 and 9)
| # | Drift | Fix |
|---|---|---|
| 1 | `docs/plan/` doesn't exist. The `save-plan` skill writes to `docs/plans/` | Use `docs/plans/` (decided) |
| 2 | CLAUDE.md milestones say "M3+: Crawler" | `M3 (P3): Domain model & persistence`, then `M4+: Crawler …` |
| 3 | CLAUDE.md layout says Domain/Application are "empty in M2" | Update the Domain line. Application stays empty until P4 |
| 4 | `ArchitectureTests.cs` comment says Domain is still empty | Reword the comment and keep the test |
| 5 | `ScraperDbContext` comment says "Entities arrive in Milestone 3" | Replace it with a real summary |
| 6 | ADR 0003 says "`jsonb` holds the structured blocks" | ADR 0010 records the per-block rows as a refinement |
| 7 | `sites.md` rate limits are still TODO | Note the seeded conservative values. The TODOs stay open |
| 8 | `Api__BaseUrl` isn't in `.env.example` | Not a secret, fixed internal host. Noted only |
| 9 | `save-plan` can't be invoked by Claude | The user types `/save-plan` |

### Domain model (`src/Scraper.Domain`, no EF attributes, BCL only)
Entities use `private set` and private parameterless constructors for EF. Callers pass time in as `DateTimeOffset`.

**Value objects**
- `Urls/NormalizedUrl`: a sealed record wrapping `Value`. `Create(string)`, `TryCreate(string, Uri? baseUri, out …)`.
  Rules: absolute http/https only. Reject userinfo, `mailto:`/`javascript:` and similar schemes, and anything over 2048 chars.
  Lowercase the scheme and host (IDN → punycode). Drop the default port and the fragment. An empty path becomes `/`.
  Remove dot segments. Strip the trailing slash (except the root). Keep the path's case.
  Drop `utm_*`, `fbclid` and `gclid`. Sort the remaining query params (ordinal) and drop an empty `?`. Uppercase percent-encoding.
- `Content/ContentHash`: 64 lowercase hex chars (SHA-256). `Compute(string canonical)`, `Parse(string)`.
- `Content/DocumentContent`: the hash input: title, vehicle name, published-at, `SiteFields` and the ordered blocks.
  `ComputeHash()` builds a canonical string: a `v1` prefix, then each part length-prefixed (`tag:len:value`).
  Whitespace is collapsed and trimmed, text is NFC, site fields are sorted by key, and null ≠ empty. Metrics, links, raw HTML, headers and timestamps aren't members (ADR 0009).
- `Sites/RateLimit` (rps, > 0 and ≤ 5), `Sites/FetchMode` { Http, Browser }, `Crawling/CrawlMode` { Full, Incremental }, plus the status enums.

**Entities**
- `Sites/SiteConfig`: `Id` (text key), `Name`, `BaseUrl`, `SeedUrls`, `AllowedPathPrefixes`, `FetchMode`, `RateLimit`, `MaxDepth` (≥ 0), `MaxPages` (> 0).
- `Crawling/CrawlJob`: `Id`, `SiteId`, `Mode`, `Status` (Pending/Running/Completed/Cancelled/Failed), `RequestedAt`, `StartedAt?`, `FinishedAt?`. Guarded transitions.
- `Crawling/CrawlTask`: `Id`, `CrawlJobId`, `Url`, `Depth`, `Status` (Queued/InProgress/Succeeded/Failed/DeadLettered), `AttemptCount`, `LastError?`, `PageId?`, `EnqueuedAt`, `CompletedAt?`.
- `Crawling/DeadLetterEntry`: `Id`, `CrawlTaskId`, `SiteId`, `Url`, `LastError`, `AttemptCount`, `DeadLetteredAt`.
- `Pages/Page`: `Id`, `SiteId`, `Url`, `FirstSeenAt`, `LastFetchedAt?`, `LastStatusCode?`, `ETag?`, `LastModified?`, `RemovedAt?`. `MarkRemoved(at)`, `MarkSeen(at, …)`.
- `Pages/PageFetch`: `Id`, `PageId`, `CrawlTaskId`, `FetchedAt`, `FetchMode`, `StatusCode`, `Headers`, `ETag?`, `LastModified?`, `RawHtml?`.
- `Pages/Link`: `Id`, `SourcePageId`, `TargetUrl`, `Text` (≤ 500), `FirstSeenAt`, `LastSeenAt`.
- `Content/Document`: `Id`, `PageId` (1:1), `Kind` { Vehicle, NewsArticle, Mod }, `CurrentVersionId?`, `FirstSeenAt`. Listing pages get no Document.
- `Content/DocumentVersion` (immutable): `Id`, `DocumentId`, `ContentHash`, `CapturedAt`, `Title`, `VehicleName?`, `PublishedAt?`, `SiteFields`, `Blocks`.
- `Content/Block` → `ContentBlock` (Heading/Paragraph/List/Code) and `TableBlock` (`Caption?`, `Header`, `Rows`).
- `Content/SiteFields` → `GtaCarsFields`, `NewswireFields`, `Gta5ModsFields` (author username only), each with `CanonicalFields()`.
- `Content/MetricsSnapshot`: `Id`, `DocumentId`, `CrawlTaskId`, `CapturedAt`, `Downloads?`, `Likes?`, `Views?`.

### ER diagram
```
sites (id text PK) ──1:N── crawl_jobs ──1:N── crawl_tasks ──0..1── dead_letter_entries
  │                                              │   │
  │                                              │   └─ page_id? ─┐
  └──1:N── pages ◄───────────────────────────────┼────────────────┘
            │  (UQ site_id,url)                  │
            ├──1:N── page_fetches  (UQ crawl_task_id) ── crawl_task_id ─┘
            ├──1:N── links         (UQ source_page_id,target_url)  [target_url = text, not FK]
            └──1:0..1── documents  (UQ page_id) ── current_version_id? ──┐
                          ├──1:N── document_versions (UQ document_id,content_hash) ◄┘
                          │            └──1:N── content_blocks (UQ document_version_id,ordinal)
                          │                       [kind=heading|paragraph|list|code|table, TPH]
                          └──1:N── metrics_snapshots (UQ crawl_task_id)
crawl_tasks: UQ (crawl_job_id, url), index (crawl_job_id, status)
dead_letter_entries: UQ crawl_task_id
document_versions: index vehicle_name
```
11 tables. Every FK is `ON DELETE RESTRICT`.

### Persistence (`src/Scraper.Infrastructure/Persistence`)
- `ScraperDbContext`: DbSets, `ApplyConfigurationsFromAssembly`, `UseSnakeCaseNamingConvention()`.
- `Configurations/*Configuration.cs`, one per entity. Converters: `NormalizedUrl`↔text, `ContentHash`↔`char(64)`, `RateLimit`↔`numeric(4,2)`, enums↔text,
  `SiteFields`↔`jsonb` (an STJ polymorphic converter in Infrastructure), `TableBlock.Rows/Header`↔`jsonb`,
  `ListItems`/`SeedUrls`/`AllowedPathPrefixes`↔`text[]`, `Headers`↔`jsonb`, with ValueComparers.
- `HasData` seeds:

| id | base | fetch | rps | depth | pages |
|---|---|---|---|---|---|
| gtacars | https://gtacars.net/ | Http | 0.5 | 3 | 1000 |
| newswire | https://www.rockstargames.com/newswire | Browser | 0.2 | 2 | 200 |
| gta5mods | https://www.gta5-mods.com/vehicles | Http | 0.5 | 2 | 1000 |

  Path prefixes: `/newswire` for Newswire and `/vehicles` for GTA5-Mods.
- Migration `AddDomainModel` goes on top of the empty `InitialCreate`.
- Package `EFCore.NamingConventions` (10.0.x).

### How each write will be an upsert (implemented in P4)
| Table | Conflict target | Action |
|---|---|---|
| sites | PK | migration only |
| crawl_jobs | PK (Guid v7) | insert; status `UPDATE … WHERE status = @expected` |
| crawl_tasks | (crawl_job_id, url) | `DO NOTHING`; `attempt_count = attempt_count + 1` |
| pages | (site_id, url) | `DO UPDATE … WHERE excluded.last_fetched_at >= pages.last_fetched_at RETURNING id` |
| page_fetches | crawl_task_id | `DO NOTHING` |
| links | (source_page_id, target_url) | `DO UPDATE SET text, last_seen_at = greatest(…)` |
| documents | page_id | `DO NOTHING`, then select the id |
| document_versions | (document_id, content_hash) | `DO NOTHING`. Blocks are inserted only if the version was inserted. Then set `current_version_id` |
| content_blocks | (document_version_id, ordinal) | written only together with a new version |
| metrics_snapshots | crawl_task_id | `DO NOTHING` |
| dead_letter_entries | crawl_task_id | `DO UPDATE SET last_error, attempt_count, dead_lettered_at` |

**Removed pages:** P4 sets `removed_at` on a 404/410 and clears it if the page comes back. There's no delete path, and RESTRICT FKs make deletes fail.

### Files touched
- New: `src/Scraper.Domain/{Urls,Content,Sites,Crawling,Pages}/*.cs`
- New: `src/Scraper.Infrastructure/Persistence/Configurations/*.cs`, `…/Converters/SiteFieldsJsonConverter.cs`, `…/Migrations/<ts>_AddDomainModel*.cs` (+ the snapshot)
- Edit: `ScraperDbContext.cs`, `Scraper.Infrastructure.csproj`, `Directory.Packages.props`
- New tests: `tests/Scraper.Domain.Tests/{NormalizedUrlTests,ContentHashTests,DocumentContentHashTests}.cs`, `tests/Scraper.Infrastructure.Tests/ModelTests.cs`
- Edit: `tests/Scraper.Application.Tests/ArchitectureTests.cs` (comment only)
- Docs: new `docs/decisions/0010-content-storage-model.md`; edit `CLAUDE.md`, `docs/compliance/sites.md`

### New settings
None. `.env.example` and compose are unchanged.

## Tests
Run all of them with `dotnet test`.

- **Step 2: unit (`Scraper.Domain.Tests/NormalizedUrlTests`)**
  - Equivalent forms map to one value (host case, default port, fragment, trailing slash, `utm_*`, param order).
  - Normalizing twice gives the same value. Path case is kept. A relative href resolves against a base.
  - Rejects: relative without a base, `ftp`/`mailto`/`javascript`, userinfo, > 2048 chars.
  - Value equality.
- **Step 3: unit (`ContentHashTests`, `DocumentContentHashTests`)**
  - SHA-256 known vector (`"abc"` → `ba7816bf…`). `Parse` round-trips, lowercases input, rejects a bad length or non-hex.
  - Whitespace-only change → same hash. Block order change → different. Table cell change → different.
  - Site-field key order doesn't matter. Null ≠ empty. Length-prefix boundaries (`"ab","c"` ≠ `"a","bc"`).
  - Different `MetricsSnapshot` values → same hash (ADR 0009).
- **Step 8: model (`Scraper.Infrastructure.Tests/ModelTests`, no DB)**
  - `Database.HasPendingModelChanges()` is false.
  - Each unique index from the ER diagram exists and is unique.
  - Every FK has `DeleteBehavior.Restrict`.
  - *(added during implementation)* `SiteFields` round-trips through the jsonb converter for all three sites.
- **All steps: architecture.** The existing `ArchitectureTests` stay green.
- **Step 7: manual E2E (migration in Docker)**
  ```bash
  docker compose down -v && docker compose up --build -d
  docker compose ps -a migrator                       # Exited (0)
  docker compose logs migrator | grep "Migrations applied"
  docker compose exec postgres psql -U scraper -d scraper -c '\dt'                    # 11 tables + __EFMigrationsHistory
  docker compose exec postgres psql -U scraper -d scraper -c '\d document_versions'   # UQ (document_id, content_hash)
  docker compose exec postgres psql -U scraper -d scraper -c 'select id, fetch_mode, requests_per_second from sites'  # 3 rows
  # Idempotency smoke test: same INSERT … ON CONFLICT (site_id, url) DO NOTHING into pages twice → count stays 1
  docker compose up -d                                 # second run: migrator exits 0
  curl localhost:8080/health                           # healthy
  ```

### Done criteria
- `dotnet build` (warnings as errors), `dotnet test` and `dotnet format --verify-no-changes` pass locally and in CI.
- Fresh `docker compose up --build`: the migrator exits 0, the tables and unique indexes exist, 3 sites are seeded, and a re-run is a no-op.
- ADR 0010 is written, CLAUDE.md and `sites.md` are updated, and the drift items are resolved.
- No store or crawling code. No new settings.

## Decisions & trade-offs
(These are recorded in the new ADR 0010 "Content storage model".)
1. **Page / Document / DocumentVersion split:** keeps crawl state apart from content. Listing pages carry no content.
2. **Site-specific fields as `jsonb` written by typed C# records.** Shared fields (`title`, `vehicle_name`, `published_at`) are typed columns, and the join key is indexed.
3. **One row per block (TPH), `jsonb` only for table cells:** each block gets a stable id for Phase 2 citations.
4. **Natural-key unique constraints plus `INSERT … ON CONFLICT`** make writes idempotent.
5. **Guid v7 keys generated in the domain:** time-ordered, no DB round trip. Sites use a readable text key.
6. **`removed_at` plus RESTRICT FKs:** removed pages stay visible and are never deleted.
7. **Enums as text:** readable in psql and safe to reorder.
8. **Canonical, domain-built hash string with a `v1` prefix.**
9. **Observation rows (fetches, metrics) unique per `crawl_task_id`:** redeliveries don't duplicate them.
10. **A new migration on top of `InitialCreate`:** committed migrations stay immutable.
11. **snake_case naming via EFCore.NamingConventions:** readable raw SQL in P4.
12. **Site config as DB rows (`HasData`).**

## Alternatives considered
1. One `pages` table with content columns: mixes concerns, and versioning gets awkward.
2. Typed per-site tables or columns: about 15 mostly-null columns or 3 extra 1:1 tables, and a migration for every field change.
3. The whole block array as one `jsonb` column: no stable per-block id.
4. Check-then-insert (race between workers). FlexLabs.Upsert (an extra dependency that hides the SQL).
5. bigint identity keys.
6. A soft-delete global query filter, which would hide removed pages from the dashboard.
7. Int enum columns.
8. Hashing JSON serializer output: option or order changes silently change every hash.
9. No constraint on fetches/metrics: redeliveries would duplicate them.
10. Regenerating `InitialCreate`.
11. Quoted PascalCase identifiers.
12. Site config in env vars or appsettings: many non-secret settings, and job FKs need a row.

### Risks
- URL normalization can merge distinct URLs (trailing slash, sorted query). P5 verifies this on the 3 sites.
- A content revert A→B→A adds no new version row. `current_version_id` points back to A. Accepted.
- Raw HTML growth in `page_fetches`. TOAST compresses it. Retention is out of scope.
- Changing the hash rules later creates one new version per page. The `v1` prefix makes this explicit.
- `jsonb` site fields aren't type-checked by Postgres. The typed records are the only writer.
- The seeded values (seed URLs, depth vs pagination) aren't verified yet. A later migration can adjust them.
- `EFCore.NamingConventions` 10.0.x availability. Fallback: explicit `ToTable`/`HasColumnName`.

## Out of scope
Store/upsert implementations, Application ports, full-text `tsvector`, vehicle-name matching across
sites, auth, and dashboard pages.

## Changes
- **2026-11-26: implementation notes.**
  - `content_blocks` uses a `block_type` discriminator (`content` / `table`) plus a `kind` column
    (heading / paragraph / list / code) instead of one combined `kind` column, because EF's TPH
    discriminator can't also be `ContentBlock.Kind`. The ER diagram's meaning is unchanged.
  - Extra Infrastructure helpers beyond the listed files: `Converters/JsonColumn.cs` (generic jsonb
    converter and comparer), `Converters/Conversions.cs` (value-object and enum mappings),
    `Configurations/SiteSeed.cs` (the seeded sites).
  - Added a model test: `SiteFields` round-trips through the polymorphic jsonb converter. The
    converter is the riskiest new mapping and the plan had no test for it.
  - CLAUDE.md also gained a short "Persistence rules (ADR 0010)" section.
  - Verification: 71 tests pass, `dotnet format` and the Release build are clean, and the Docker
    checks in the Tests section passed (run with `--env-file .env.example` under a separate compose
    project, since there's no local `.env`). The migrator's first run logs one EF `fail:` line for
    the not-yet-existing `__EFMigrationsHistory` table; that's EF's normal behavior on an empty database.
