# Site compliance notes

How each target site is crawled politely and within its rules. The crawler identifies itself with
`Crawler__UserAgent`, which includes a contact address. Anything marked **TODO: verify** has not
been confirmed yet.

| Site | robots.txt | Terms | Rate limit | Notes |
|---|---|---|---|---|
| **GTACars.net** | Disallows only `/api/` for generic crawlers; named SEO bots are blocked entirely. | No terms page found: `/terms` returns 404; there's only an About page. | ≤ 1 req/s, or slower (small hobby site). Seeded at 0.5 req/s. | Never call `/api/`. |
| **Rockstar Newswire** (`rockstargames.com/newswire`) | No rule for generic crawlers; it only blocks named bots (ia_archiver, CCBot, GPTBot, ClaudeBot, Meta-ExternalAgent and others). | A search of the Terms of Service for robot / spider / scrape / crawl clauses found none. **TODO: verify** with a full read. | Very gentle. **TODO: verify** exact value. Seeded at 0.2 req/s. | Stay on `/newswire` pages only; never touch Social Club or anything that needs a login. **Headless check passed (2026-11-27):** headless Chromium (Playwright 1.65.1) with our User-Agent, images/fonts/media blocked, got HTTP 200, title "Newswire - Rockstar Games" and 21 article links (`/newswire/article/…`) in about 2.4 s, with no challenge page or captcha. Two page loads in total. If a challenge ever appears, do not bypass it; pick a fallback site. **AI crawlers:** robots.txt blocks named AI bots (ClaudeBot, GPTBot, CCBot, cohere-ai, Meta-ExternalAgent and others). Our bot isn't one of them, so the rules allow it, but Phase 2 runs RAG over this content: keep the Newswire crawl small (≤ 200 pages, 0.2 req/s) and mention this in the report. |
| **GTA5-Mods.com** | Disallows `/*/*/download/*` in each category and `/search/*`; lists a sitemap. | Terms of Use don't mention automated access. | **TODO: verify** a value; gentle by default. Seeded at 0.5 req/s. | Behind Cloudflare. A curl with our User-Agent returned 200 (tested 2026-11-23). If a challenge ever appears, stop and don't bypass it. Never follow download links. Store mod authors' usernames only as the author field; don't crawl profiles. |

## Ruled out

| Site | Reason |
|---|---|
| GTA Wiki (Fandom) | Its Terms of Use forbid robots, spiders and scrapers without written permission. |
| GTALens | Behind a Cloudflare challenge page. |
