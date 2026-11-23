# ADR 0004: Redis for shared, fast-changing crawler state

**Status:** Accepted

## Context
All workers must share per-domain rate limits, a "seen URL" set and liveness information. These
checks happen on every URL, so they must be atomic and fast.

## Decision
Use Redis 7:
- a token bucket per domain, implemented as an atomic Lua script
- a seen set with `SADD`
- heartbeat keys with a TTL (`worker:heartbeat:{hostname}`)

## Rejected alternative
**Counters and locks in PostgreSQL.** Possible, but every URL would cost row locks and round trips
on the primary store, and atomic token-bucket logic gets awkward in SQL.

## Consequences
- `SADD` returns 1 only for a new member, so "check and mark as seen" is one atomic step.
- A Lua script runs atomically, so concurrent workers can't overspend a domain's rate budget.
- Redis is not the source of truth: Postgres unique constraints still prevent duplicates if Redis is
  wiped. We'd lose only some efficiency, never correctness.
- `activeWorkers` in `/health` counts heartbeat keys with `SCAN` (never `KEYS`, which blocks Redis).
