# ADR 0010: Content storage model

**Status:** Accepted

## Context
Three sites with different shapes have to be stored so that re-crawls are idempotent, removed pages
keep their history, and Phase 2 RAG gets structured content (blocks, links, versions), never
flattened text. ADR 0003 chose PostgreSQL and said `jsonb` holds the structured blocks; this ADR
fixes how. ADR 0009 defines what goes into the content hash.

## Decision
1. **Page / Document / DocumentVersion.** `pages` holds crawl state per (site, normalized URL).
   `documents` holds the extracted content of a content page (1:1, listing pages have none).
   `document_versions` are immutable snapshots, unique per (document, content hash).
2. **Site-specific fields in one `jsonb` column**, written only through typed C# records
   (`GtaCarsFields`, `NewswireFields`, `Gta5ModsFields`) with a `"site"` discriminator. Fields every
   site shares (`title`, `vehicle_name`, `published_at`) are typed columns, and `vehicle_name`, the
   Phase 2 join key, is indexed.
3. **One row per block** in `content_blocks` (table-per-hierarchy: headings, paragraphs, lists,
   code and tables). List items are `text[]`, and only table headers and cells are `jsonb`. This
   refines ADR 0003: blocks are still structured, not one `jsonb` array per version.
4. **Natural keys with unique constraints** on every table that a worker writes, so each write is
   an `INSERT … ON CONFLICT` upsert and a redelivered message changes nothing. Fetches and metrics
   snapshots are unique per crawl task.
5. **Guid v7 keys** created in the domain (time-ordered, no database round trip). Sites use a
   readable text key (`gtacars`, `newswire`, `gta5mods`).
6. **Never delete.** A page that disappears gets `removed_at`. Every foreign key is
   `ON DELETE RESTRICT`, so an accidental delete fails instead of cascading.
7. **Enums stored as text**, and tables/columns in snake_case (EFCore.NamingConventions), so data
   and raw SQL stay readable.

## Rejected alternatives
- **Typed columns or one table per site** for site-specific fields: about 15 mostly-null columns
  (or 3 extra 1:1 tables) and a migration for every new field, for data Phase 2 mostly reads as text.
- **The whole block list as one `jsonb` column per version:** blocks get no stable id that Phase 2
  chunks can cite, and per-block queries get awkward.
- **Check-then-insert** instead of `ON CONFLICT`: two workers can both pass the check.
- **A soft-delete query filter:** it would hide removed pages from the dashboard, which should show them.

## Consequences
- Re-crawling unchanged content adds no version; a second job adds only new observations (one fetch
  and one metrics snapshot per page).
- Postgres doesn't type-check the `jsonb` site fields; the typed records are the only writer.
- A content revert (A → B → A) points the document back to version A instead of adding a row, so
  the history doesn't record when the revert happened.
- EF Core has no native upsert, so the write paths (P4) use explicit SQL.
