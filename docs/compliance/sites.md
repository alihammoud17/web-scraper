# Site compliance notes

How each target site is crawled politely and within its rules. The crawler identifies itself with
`Crawler__UserAgent`, which includes a contact address. Anything marked **TODO: verify** has not
been confirmed yet.

| Site | robots.txt | Terms | Rate limit | Notes |
|---|---|---|---|---|
| **GTACars.net** | Disallows only `/api/` for generic crawlers; named SEO bots are blocked entirely. | No terms page found: `/terms` returns 404; there's only an About page. | ≤ 1 req/s, or slower (small hobby site). | Never call `/api/`. |
| **Rockstar Newswire** (`rockstargames.com/newswire`) | No rule for generic crawlers; it only blocks named bots (ia_archiver, CCBot, GPTBot, ClaudeBot, Meta-ExternalAgent and others). | A search of the Terms of Service for robot / spider / scrape / crawl clauses found none. **TODO: verify** with a full read. | Very gentle. **TODO: verify** exact value. | Stay on `/newswire` pages only; never touch Social Club or anything that needs a login. **TODO: verify** in Milestone 6 that headless Playwright gets the page without a bot challenge. If it's challenged, do not bypass it; pick a fallback site. |
| **GTA5-Mods.com** | Disallows `/*/*/download/*` in each category and `/search/*`; lists a sitemap. | Terms of Use don't mention automated access. | **TODO: verify** a value; gentle by default. | Behind Cloudflare. A curl with our User-Agent returned 200 (tested 2026-11-23). If a challenge ever appears, stop and don't bypass it. Never follow download links. Store mod authors' usernames only as the author field; don't crawl profiles. |

## Ruled out

| Site | Reason |
|---|---|
| GTA Wiki (Fandom) | Its Terms of Use forbid robots, spiders and scrapers without written permission. |
| GTALens | Behind a Cloudflare challenge page. |
