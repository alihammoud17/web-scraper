# P4: Crawl core and politeness

- **Date:** 2026-11-27
- **Branch:** `p4-crawl-core` (to be created from `main`, which already contains P3)
- **Goal:** A correct, polite fetch pipeline for a single URL (scope → robots.txt → shared rate
  limit → resilient HTTP fetch), behind Application ports with Infrastructure adapters, plus a
  time-boxed check that the Rockstar Newswire loads in headless Playwright.
- **Status:** in-progress
- **Approval:** approved in plan mode

## Context
P3 (merged as PR #1) gave us the domain model and schema. P4 builds the polite fetch pipeline that
later steps (frontier, queue consumers, parsers) call through ports. Out of scope: queue consumers,
frontier/depth logic, parsers and DB upserts.

## Steps
- [x] 1. `docs: close out M3 and align docs for P4`
- [x] 2. `docs(compliance): record Rockstar Newswire headless check`
- [x] 3. `feat(domain): add RFC 9309 robots.txt parser with fixture tests`
- [x] 4. `feat(domain): add URL accessors and site scope check`
- [x] 5. `feat(application): add crawl ports and polite fetch pipeline`
- [x] 6. `feat(infrastructure): add Redis seen-URL store`
- [ ] 7. `feat(infrastructure): add Redis token-bucket rate limiter`
- [ ] 8. `feat(infrastructure): add cached robots.txt policy`
- [ ] 9. `feat(infrastructure): add resilient HTTP fetcher with per-attempt rate limiting`
- [ ] 10. `ci: run Redis-backed tests against a Redis service container`
- [ ] 11. `feat(testsite): add robots.txt, pages and request timestamp log`
- [ ] 12. `feat(worker): add fetch probe and seed testsite for the rate-limit proof`
- [ ] 13. `docs: add ADR 0011 and rate-limit proof to README`

Each step is one commit and also updates `docs/CHANGELOG.md`.

### Drift check (step 1, and wherever noted)
Live robots.txt files were checked 2026-11-27. Rockstar has 8 named-bot groups, no `*` group, and a
trailing space after `Applebot-Extended`. GTACars has `*` → `Disallow: /api/`, plus one group with
8 SEO bots. GTA5-Mods has `*` with `/…/*/download/*` and `/search/*`.

| # | Drift | Effect on P4 / fix |
|---|---|---|
| 1 | The brief says `docs/plan/`; plans live in `docs/plans/` (decided in P3) | Save to `docs/plans/` |
| 2 | URL normalization was **done in P3** (`NormalizedUrl` + 34 tests, same rules) | Reuse it. P4 adds only the `Host` / `Authority` / `PathAndQuery` accessors (+ tests) |
| 3 | Seeding the 3 sites was **done in P3** | P4 adds `SiteConfig.IsInScope(url)` and seeds a 4th row, `testsite`, for the rate-limit proof |
| 4 | P3 said "P4 writes the upsert stores next to the consumers", but P4 has no consumers | Upsert stores move to **P5** with the consumers. The P3 plan gets a note |
| 5 | P3 risk: "P4 defines depth vs pagination" | Moves to P5 (frontier) |
| 6 | P3 was merged, but CLAUDE.md still shows M3 unticked, and the CHANGELOG `Unreleased` items weren't folded into the M3 section | Tick M3. Move those items into `[M3 / P3] … 2026-11-27`. Start a fresh `Unreleased` |
| 7 | CLAUDE.md "M4+: Crawler" lumps everything together. M6 is the Newswire check | Becomes `M4 (P4): crawl core & politeness (+ Newswire spike)`, `M5+: frontier, queue consumers, parsers, stores`. M6 is folded into P4 |
| 8 | TestSite comment says "pages are added in Milestone 3" | P4 adds the pages; update the comment |
| 9 | ADR 0004 describes one global "seen set" | One set **per crawl job**, so incremental jobs can revisit URLs (ADR 0011) |
| 10 | `Microsoft.Extensions.Http.Resilience` is referenced but unused | P4 uses it (no new package) |
| 11 | `sites.md` Newswire row: "TODO verify headless without a challenge" | Updated with the spike result and the AI-bot note |

### Newswire spike (step 2, time-boxed: 1 h)
- **Setup:** a throwaway .NET console in the scratchpad (never committed), run in `mcr.microsoft.com/playwright/dotnet:v1.65.1-noble`. Fallback: install Chromium locally with Playwright.
- **One page load** of `https://www.rockstargames.com/newswire`:
  - our User-Agent;
  - images, fonts and media blocked;
  - wait up to 30 s for `a[href*="/newswire/article/"]`;
  - record the HTTP status, the title, the number of article links and a screenshot;
  - look for challenge signs ("Just a moment", "Access denied", captcha iframes, 403).
- **If it passes:** record the result in `sites.md`, this plan's Changes section and the CHANGELOG, and tick M6.
- **If it's challenged:** **stop**, don't retry or bypass anything, and propose fallback Site B candidates. P4 continues without Site B.
- **AI-bot note (decision: proceed and flag it):** `sites.md` and the report record that Rockstar blocks named AI crawlers, that our bot isn't one of them, and that Phase 2 RAG creates a tension. The Newswire crawl stays small (200 pages, 0.2 req/s).

### Ports (step 5, `src/Scraper.Application/Crawling`)
```csharp
IRobotsPolicy   Task<bool> IsAllowedAsync(NormalizedUrl url, CancellationToken ct)
ISeenUrlStore   Task<bool> TryMarkSeenAsync(Guid crawlJobId, NormalizedUrl url, CancellationToken ct) // true = first time
IRateLimiter    Task AcquireAsync(string host, RateLimit limit, CancellationToken ct)                 // waits for a token
IFetcher        Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken ct)
ISiteCatalog    Task<SiteConfig?> FindByHostAsync(string host, CancellationToken ct)
```
- `FetchRequest(NormalizedUrl Url, string? ETag, DateTimeOffset? LastModified)`.
- `FetchResult`: status code, lowercased headers, body?, ETag, Last-Modified, `IsNotModified`, `RedirectLocation?`, `RetryAfter?` and the elapsed time.
- **`PoliteFetcher`** (the use case) runs the pipeline:
  1. Look up the site by host. An unknown host → `Blocked(UnknownSite)`.
  2. `IsInScope` → `Blocked(OutOfScope)`.
  3. Robots → `Blocked(RobotsDisallowed)`.
  4. `IFetcher` → `Fetched` / `NotModified` / `Redirected` / `Failed`.

  A blocked URL never reaches the network.

### Domain additions (steps 3–4, pure)
- **`Robots/RobotsTxt`** follows RFC 9309:
  - **Parsing:** skip the BOM, accept CRLF/LF, strip `#` comments, trim, match keys case-insensitively. Consecutive `user-agent` lines form one group, and a `user-agent` line after rules starts a new group. Read at most 500 KiB.
  - **Group selection:** groups whose user-agent equals our **product token** (the part of `Crawler__UserAgent` before `/`, compared case-insensitively and trimmed), merged if there are several. Otherwise the `*` group(s). If neither exists, **allow all**.
  - **Matching:** path plus query. `*` matches any sequence and `$` anchors the end. The **longest match wins**, and `Allow` wins a tie. Percent-encoding is normalized. `/robots.txt` is always allowed, and an empty `Disallow` restricts nothing.
  - **`FromResponse(status, body)`:**
    - 2xx → parse the body;
    - 4xx (incl. 404) → **allow all**;
    - 5xx or network error → **disallow all** for now.
  - `crawl-delay` is ignored (it isn't in the RFC, and none of the three sites uses it).
- `NormalizedUrl.Host`, `.Authority`, `.PathAndQuery`.
- `SiteConfig.IsInScope(url)`: same scheme and authority as `BaseUrl`, and the path is under one of `AllowedPathPrefixes` (an empty list means the whole site).

### Adapters (steps 6–9, `src/Scraper.Infrastructure/Crawling`)
- **`RedisSeenUrlStore`:** `SADD crawl:{jobId}:seen <url>`. A reply of 1 means "new", in one atomic step. The key expires after 7 days.
- **`RedisRateLimiter`:** a token bucket in an atomic Lua script (an embedded resource, `token-bucket.lua`).
  - **State:** key `ratelimit:{host}` → hash `{tokens, ts}`. Capacity is 1, so requests are evenly spaced.
  - **Clock:** the script uses Redis **`TIME`**.
  - **Result:** it returns 0 (token taken) or the number of milliseconds to wait. The client waits that long, plus a small jitter, and tries again.
- **`RedisRobotsPolicy`:** looks up the cache `robots:{scheme}://{authority}` → `{status, body}`.
  - **On a miss:** fetch `/robots.txt` through the crawler `HttpClient` (so it's rate-limited).
  - **Cache lifetime:** **24 h** after a 2xx or 4xx, **5 min** after a 5xx or network error.
  - **Parsing:** done with `RobotsTxt` on every check.
- **`SiteCatalog`:** EF query on `sites`, cached in memory for 5 minutes, matching by `BaseUrl.Host`.
- **`HttpFetcher`:** the named `HttpClient` "crawler" with this handler chain (outermost first):
  ```
  resilience "retry"     total timeout 5 min → retry (3 attempts, exponential + jitter, base 2 s, honors Retry-After)
    RateLimitingHandler  ← every attempt (including retries and robots.txt fetches) takes a Redis token
      resilience "attempt" timeout 30 s per attempt
        SocketsHttpHandler  AllowAutoRedirect=false, gzip/br decompression, 10 MB response cap
  ```
  - **Retries on:** 429, 503, `HttpRequestException` and per-attempt timeouts. Other statuses are returned without retrying.
  - **Retry-After:** honored. A `Retry-After` over **60 s** isn't retried: it's returned as `RetryAfter`.
  - **User-Agent:** `Crawler__UserAgent` (required). Startup fails if it's missing.
  - **Conditional requests:** `If-None-Match` / `If-Modified-Since` when validators are given. A 304 → `IsNotModified`.
  - **Redirects aren't followed.** The `Location` is returned.
  - **Off-site hosts:** a request to a host not in `sites` is refused in `RateLimitingHandler`.
  - **Tuning:** the values are code defaults in `CrawlerHttpOptions`, not environment settings.

### Rate-limit proof mode (steps 11–12)
- **`FetchProbeService`:** a worker hosted service, **off unless `Crawler__ProbeUrl` is set**. It loops on `PoliteFetcher` for that URL and logs each result.
- **TestSite gains:**
  - a request log line `HIT <UTC ISO time> <path> <user-agent>`;
  - `/robots.txt` (`Disallow: /private/`);
  - `/pages/{n}`;
  - `/status/{code}` (adds `Retry-After` for 429/503).
- **Migration `AddTestSite`:** seeds `testsite` → `http://testsite:8080/`, Http, **1 req/s**, depth 3, 100 pages.
- **`tools/check-rate.sh`:** parses `docker compose logs testsite` and prints the request count, the minimum gap and the maximum requests per second.

### Files touched
- **Domain:**
  - `src/Scraper.Domain/Robots/RobotsTxt.cs`, `RobotsPattern.cs`
  - edits to `Urls/NormalizedUrl.cs` and `Sites/SiteConfig.cs`
- **Application:**
  - `src/Scraper.Application/Crawling/{IRobotsPolicy,ISeenUrlStore,IRateLimiter,IFetcher,ISiteCatalog}.cs`
  - `FetchRequest.cs`, `FetchResult.cs`, `PoliteFetchOutcome.cs`, `PoliteFetcher.cs`
  - `DependencyInjection.cs` (`AddApplication()`)
- **Infrastructure:**
  - `src/Scraper.Infrastructure/Crawling/{CrawlerHttpOptions,HttpFetcher,RateLimitingHandler,RedisRateLimiter,RedisSeenUrlStore,RedisRobotsPolicy,SiteCatalog}.cs`, `token-bucket.lua` (embedded)
  - `DependencyInjection.cs` (`AddCrawler()`)
  - `Persistence/Configurations/SiteSeed.cs` + migration `AddTestSite`
- **Worker:** `src/Scraper.Worker/FetchProbeService.cs`, `Program.cs`
- **TestSite:** `tools/Scraper.TestSite/Program.cs`
- **Tools:** `tools/check-rate.sh`
- **Tests:**
  - `tests/Scraper.Domain.Tests/{RobotsTxtTests,SiteScopeTests}.cs`, plus additions to `NormalizedUrlTests.cs`
  - `tests/Scraper.Domain.Tests/Fixtures/robots/{rockstar,gtacars,gta5mods}.txt` (captured files with a `# Captured 2026-11-27 from <url>` comment)
  - `tests/Scraper.Application.Tests/PoliteFetcherTests.cs`
  - `tests/Scraper.Infrastructure.Tests/{HttpFetcherTests,RedisRateLimiterTests,RedisSeenUrlStoreTests,RedisRobotsPolicyTests,RedisFactAttribute}.cs`
- **Config:** `.env.example`, `docker-compose.yml` (`Crawler__ProbeUrl` for the worker), `.github/workflows/ci.yml` (redis service + `SCRAPER_TEST_REDIS`)
- **Docs:**
  - new `docs/decisions/0011-politeness-pipeline.md`
  - `CLAUDE.md`, `docs/CHANGELOG.md`, `README.md` (Manual testing: the probe proof), `docs/compliance/sites.md`
  - the P3 plan (note on stores moving to P5)

### New settings
- `.env.example`: `CRAWLER_PROBE_URL=` (empty = probe off; e.g. `http://testsite:8080/pages/1` for the proof).
- `docker-compose.yml`: `Crawler__ProbeUrl: ${CRAWLER_PROBE_URL:-}` on the worker. It's optional, so it isn't `:?missing`.
- `SCRAPER_TEST_REDIS`: test-only (CI and local), documented in README, not in `.env.example`.

## Tests
xUnit, plain asserts, never live sites. Run them with `dotnet test`. Redis-backed tests need
`SCRAPER_TEST_REDIS=localhost:6381` and `docker compose up -d redis`.

- **Step 3: unit (`Scraper.Domain.Tests/RobotsTxtTests`, using the fixtures)**
  - **Rockstar:** our token is allowed everywhere (no `*` group). `ClaudeBot` and `CCBot` are disallowed on `/`. `Applebot-Extended` matches despite the trailing space.
  - **GTACars:** our bot: `/api/x` disallowed, `/gta5/adder` allowed. `AhrefsBot` and `Seekport Crawler` (multi-agent group) disallowed on `/`.
  - **GTA5-Mods:** `/vehicles/adder/download/123` and `/search/adder` disallowed. `/vehicles/adder` and `/search` allowed.
  - **RFC cases (inline strings):**
    - longest match wins, and `Allow` wins a tie;
    - `$` anchor;
    - comments, CRLF, BOM;
    - case-insensitive keys and user-agents;
    - merging several groups with the same agent;
    - an empty `Disallow` restricts nothing;
    - `/robots.txt` is always allowed;
    - percent-encoding is treated as equivalent;
    - the 500 KiB cap.
  - **`FromResponse`:** 200 → parse; 404 or 403 → allow all; 500 or 503 → disallow all.
- **Step 4: unit (`SiteScopeTests`, `NormalizedUrlTests` additions)** *(+ `Reading_a_part_does_not_change_equality`, added during implementation)*
  - **Scope:**
    - same host and prefix → in scope;
    - other host, other scheme, or outside the prefixes → out of scope;
    - empty prefixes → the whole site.
  - **Accessors:** `Host`, `Authority`, `PathAndQuery`.
- **Step 5: unit (`Scraper.Application.Tests/PoliteFetcherTests`, fakes for every port)**
  - Unknown site, out of scope, or robots-disallowed → blocked, and the fetcher is never called.
  - An allowed URL → fetched with the ETag and Last-Modified passed through.
  - 304 → `NotModified`. 3xx → `Redirected(location)`.
- **Step 6: integration, real Redis (`RedisSeenUrlStoreTests`, `[RedisFact]`)**
  - First add → true, second → false.
  - 100 concurrent adds of one URL → exactly one true.
  - Separate jobs don't share entries.
  - *(added during implementation)* The set expires after 7 days.
- **Step 7: integration, real Redis (`RedisRateLimiterTests`)**
  - 8 limiters, each with its own connection, at 5 req/s for 3 s → ≤ 16 tokens total.
  - The gap between consecutive tokens is ≥ 190 ms.
- **Step 8: integration, real Redis (`RedisRobotsPolicyTests`)**
  - The second check makes no HTTP call.
  - The TTL is about 24 h after a 200 and about 5 min after a 503.
- **Step 9: unit (`Scraper.Infrastructure.Tests/HttpFetcherTests`, fake primary handler + recording fake rate limiter)**
  - Sends the User-Agent, `If-None-Match` and `If-Modified-Since`. A 304 maps to not-modified.
  - **Retried:** 503 three times then 200 → succeeds after 3 attempts. `HttpRequestException` and a per-attempt timeout are retried.
  - **Not retried:** 500 and 404.
  - `429` with `Retry-After: 1` → the second attempt comes ≥ 1 s after the first.
  - `Retry-After: 120` → not retried, and `RetryAfter` is set.
  - Each attempt takes a rate-limit token (token count = attempt count).
  - A redirect isn't followed. An unknown host is refused. A response over 10 MB is rejected.
- **Step 10: CI.** The Redis-backed tests run against a `redis:7` service container (`SCRAPER_TEST_REDIS` set in the workflow). Locally they're skipped unless the variable is set.
- **Steps 11–12: E2E shared rate-limit proof (manual, recorded in README)**
  ```bash
  CRAWLER_PROBE_URL=http://testsite:8080/pages/1 docker compose up --build -d --scale worker=8
  sleep 60 && tools/check-rate.sh     # ≈ 60 requests, min gap ≈ 1.0 s, max 1 req in any second
  docker compose logs testsite | grep HIT | head   # timestamps from all 8 workers, evenly spaced
  ```
- **All steps:** the architecture tests stay green (Application has no Redis/HTTP references).

### Done criteria
- The Newswire spike has a recorded result (pass, or challenged with Site B candidates proposed). Nothing was bypassed.
- All the tests above pass locally and in CI, with the Redis tests running in CI. The format check and Release build are clean.
- **E2E:** 8 workers against TestSite at 1 req/s produce ≤ 1 request in any second, and `check-rate.sh` shows a minimum gap of about 1 s.
- `docker compose up` without `CRAWLER_PROBE_URL` behaves exactly as before (probe off).
- ADR 0011 is written. CHANGELOG, README Manual testing, CLAUDE.md and `sites.md` are updated. The drift items are fixed.

## Decisions & trade-offs
(These are recorded in the new ADR 0011 "Politeness pipeline".)
1. **Shared token bucket in a Redis Lua script:** all workers draw from one budget per host.
2. **Redis `TIME` inside the script:** one clock for every worker.
3. **Rate-limit every attempt:** the handler sits inside the retry pipeline, so retries and robots.txt fetches also take tokens.
4. **Per-attempt timeout inside the rate limiter, total timeout outside:** waiting for a token never counts as a timeout.
5. **A custom resilience pipeline:** retry only on 429/503/timeouts/connection errors, honoring Retry-After (capped at 60 s).
6. **Don't follow redirects automatically:** targets go through scope, robots and rate limiting.
7. **Robots: 4xx → allow all, 5xx → disallow all, cache 24 h / 5 min** (RFC 9309).
8. **One seen set per crawl job:** `SADD` is check-and-mark in one atomic step.
9. **Refuse hosts not in `sites`.**
10. **Redis-backed tests:** a CI service container plus a local opt-in.

## Alternatives considered
1. **A per-worker delay (`Task.Delay(1/rate)`):** each worker only knows its own requests, so N workers send N× the rate (8 workers at 1 req/s = 8 req/s). Scaling would break politeness.
2. **Worker clocks:** drift between containers would let workers refill tokens too early.
3. **Taking a token once before `FetchAsync`:** retries and robots.txt fetches would bypass the limit, exactly when a site is struggling.
4. **One attempt timeout around everything:** a long token wait (8 workers at 0.2 req/s, about 40 s) would count as a timeout and trigger pointless retries.
5. **`AddStandardResilienceHandler`:** it also retries 500/408 and adds a circuit breaker and rate limiter with defaults we'd have to explain or fight.
6. **The HttpClient's automatic redirects:** a redirect to another host or path would skip scope, robots and rate limiting.
7. **Treating every robots.txt failure as allow-all:** a struggling server would get crawled harder.
8. **A global seen set** (incremental jobs couldn't revisit URLs). **A Bloom filter** (false positives silently skip pages, and RedisBloom isn't in `redis:7-alpine`).
9. **Allowing any host:** a bad link could hit an unconfigured site with no rate limit.
10. **Testcontainers** (an extra dependency, as decided in P3). **Mocking Redis** (it can't test the Lua script).

### Risks
- **Newswire challenged or its markup changes:** the spike stops early and P4 continues without Site B.
- **AI-bot tension on Rockstar:** flagged in the docs. Keep the crawl small. Another Site B is still possible.
- **Starvation:** the token bucket doesn't guarantee fairness between workers. Acceptable.
- **Redis down:** fetches fail (no requests go unthrottled), and robots fail closed.
- **Long rate-limit waits** with many workers on slow sites. The 5 min total timeout bounds them, and the frontier (P5) should spread work.
- **Retry-After over 60 s** isn't honored inside a worker. It's surfaced for re-queueing in P5.
- **The Playwright image may not include the .NET SDK** (spike only). Fallback: install Chromium locally.
- **robots.txt changes after capture:** the fixtures are dated, and the live file is read at runtime (24 h cache).

## Out of scope
RabbitMQ consumers, the frontier and depth/pagination semantics, link extraction, parsers, DB
upsert stores (P5), Playwright fetching in production (beyond the spike), auth, dashboard.

## Changes
- **2026-11-27: Newswire headless check (step 2) passed.**
  - Ran a throwaway console (scratchpad, not committed) in the `mcr.microsoft.com/playwright/dotnet:v1.65.1-noble`
    image, which includes .NET SDK 10.0.404, so the local-install fallback wasn't needed. The files were copied
    into the container with `docker cp`, because Docker Desktop can't mount the scratchpad path.
  - Result: HTTP 200, title "Newswire - Rockstar Games", 21 distinct `/newswire/article/…` links, no challenge
    text, no captcha frames, about 2.4 s.
  - **Two page loads instead of one:** the first load stopped as soon as the first article link appeared (1 link,
    and the screenshot showed the grid not yet rendered). The second load also waited for at least 6 links, to
    confirm the list itself renders. Still gentle, and nothing was retried or worked around.
  - Site B stays Rockstar Newswire. The AI-crawler note is recorded in `sites.md`.
- **2026-11-27: step 4, extra accessors and a regression test.**
  - Added `NormalizedUrl.Scheme` and `.Path` besides the planned `Host` / `Authority` / `PathAndQuery`:
    `IsInScope` needs the scheme and the path without the query.
  - Bug caught on the way: caching the parsed `Uri` in a field broke record equality (records compare
    every field). Wrote the failing test `Reading_a_part_does_not_change_equality` first, then removed the cache.
- **2026-11-27: step 5, ports keyed by authority, no `AddApplication()`.**
  - `ISiteCatalog.FindByAuthorityAsync` and `IRateLimiter.AcquireAsync(authority, …)` take the **authority**
    (host plus non-default port) instead of the bare host. `IsInScope` compares authorities, and TestSite
    (`testsite:8080`) would otherwise be indistinguishable from another port on the same host.
  - No `Scraper.Application/DependencyInjection.cs`: it would need a new package
    (`Microsoft.Extensions.DependencyInjection.Abstractions`) for a single registration. `PoliteFetcher` is
    registered in Infrastructure's `AddCrawler()` (step 9) instead, which already references Application.
- **2026-11-27: step 6, one extra test.**
  - Added `The_set_expires_after_a_week`: the plan states a 7-day expiry but had no test for it.
  - The expiry is set with `EXPIRE … NX` on the first add only, so it counts from the job's first URL.
