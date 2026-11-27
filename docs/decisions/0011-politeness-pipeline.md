# ADR 0011: Politeness pipeline (robots.txt, shared rate limit, resilient fetch)

**Status:** Accepted

## Context
Any number of workers (`--scale worker=N`) fetch pages from three small sites. Each site must see at
most its configured request rate, whatever N is. Every URL must be allowed by the site's robots.txt
and inside the site's crawl scope. Transient failures (429, 503, timeouts, dropped connections) need
retries that make things gentler for a struggling server, not harsher. ADR 0004 chose Redis for this
shared state; this ADR fixes how the pieces fit together.

## Decision
A single-URL pipeline behind Application ports (`PoliteFetcher`):
**known site → in scope → robots.txt → fetch**. A URL that fails a check is never requested.

1. **Shared token bucket in a Redis Lua script** (`token-bucket.lua`), one bucket per site authority,
   capacity 1 (evenly spaced requests). The script runs atomically and reads the clock with Redis
   `TIME`, so all workers share one budget and one clock.
2. **Every attempt takes a token.** The HTTP handler chain, outermost first:
   total timeout (5 min) → retry (429, 503, timeouts, connection errors; 3 retries, exponential
   backoff with jitter, honors `Retry-After` up to 60 s) → **rate-limit handler** → per-attempt
   timeout (30 s) → network. Retries and robots.txt fetches are throttled too, and waiting for a token
   never counts as a timed-out attempt.
3. **Redirects are not followed automatically.** The fetcher returns the `Location`, so the target goes
   through scope, robots and rate limiting like any other URL. Hosts that aren't configured sites are refused.
4. **robots.txt per RFC 9309:** our product token's group, else `*`, else allow all; longest match
   wins, Allow wins ties. 2xx is parsed, 4xx means allow all, 5xx or a network error means disallow
   all. Cached in Redis for 24 h (2xx/4xx) or 5 min (5xx/unreachable).
5. **One seen set per crawl job** (`SADD crawl:{jobId}:seen`): check-and-mark in one atomic step;
   incremental jobs can revisit URLs. This refines ADR 0004's single seen set.
6. **A `Retry-After` beyond 60 s isn't waited out in a worker**: the response is returned with
   `RetryAfter`, so the URL can be re-queued instead of blocking a worker.

## Rejected alternatives
- **A per-worker delay** (`Task.Delay(1 / rate)` in each worker): each worker only knows its own
  requests, so N workers send N× the rate (8 workers at 1 req/s = 8 req/s). Scaling would break politeness.
- **Taking one token before the whole fetch:** retries and robots.txt requests would bypass the limit,
  exactly when a site is struggling.
- **`AddStandardResilienceHandler`:** it also retries 500/408 and adds a circuit breaker and a rate
  limiter with defaults we'd have to explain or fight.
- **Treating every robots.txt failure as allow-all:** a struggling server would get crawled harder.
- **A Bloom filter for the seen set:** false positives silently skip pages, and RedisBloom isn't in
  `redis:7-alpine`.

## Consequences
- Politeness holds under scaling: proved with 8 workers against TestSite (`tools/check-rate.sh`).
- If Redis is down, no token can be taken, so fetches fail instead of going out unthrottled, and
  robots.txt checks fail closed.
- The bucket isn't fair between workers (one may wait longer), which is acceptable: the site's
  limit matters, not which worker sends the request.
- Long waits are possible with many workers on a slow site; the 5 min total timeout bounds them, and
  the frontier (P5) should spread work across sites.
