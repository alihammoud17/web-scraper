# ADR 0003: PostgreSQL 17 as the primary store

**Status:** Accepted

## Context
We store pages, structured content blocks, links, content versions and metrics snapshots. The
dashboard needs real full-text search, and workers may process the same URL twice (redelivery), so
writes must be idempotent.

## Decision
Use PostgreSQL 17 through EF Core (Npgsql provider). Schema changes go through EF migrations applied
by a one-shot `migrator` container.

## Rejected alternative
**MongoDB.** It suits document-shaped blocks, but its text search is weaker (no equivalent of
`ts_headline` snippets) and we would lose relational constraints for links and versions.

## Consequences
- `tsvector` + GIN index + `ts_headline` give ranked search with highlighted snippets.
- `jsonb` holds the structured blocks without flattening them, which Phase 2 RAG needs.
- `INSERT … ON CONFLICT` and unique constraints make redelivered messages harmless (idempotent writes).
- Schema must be migrated. The migrator runs before the API and workers start.
