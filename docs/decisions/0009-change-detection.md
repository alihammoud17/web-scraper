# ADR 0009: Change detection by hashing cleaned content

**Status:** Accepted

## Context
Pages are re-crawled to detect updates, and each real change should create a new content version.
But raw HTML changes on every request: tracking scripts, CSRF tokens, ad slots and rendered
markup. Counters such as downloads, likes and views on GTA5-Mods also change constantly.

## Decision
Compute the content hash over the **cleaned structured blocks and extracted fields** (e.g. vehicle
class, price, top speed, table cells), normalised for whitespace. A new version is stored only
when this hash changes. **Volatile counters** (downloads, likes, views) go into a separate
**metrics snapshot** table, with a timestamp, and are **excluded from the hash**.

## Rejected alternative
**Hashing the raw HTML.** It's trivial to implement, but nearly every crawl would produce a "new
version" that differs only in noise, which bloats storage and makes the version history useless.

## Consequences
- Versions reflect real content changes. Phase 2 can trust the version history.
- Counter trends are still kept, as a time series in the snapshot table.
- The cleaning and extraction rules decide what counts as a change, so they need tests.
