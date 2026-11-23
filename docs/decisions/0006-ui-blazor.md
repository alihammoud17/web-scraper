# ADR 0006: Blazor Web App (Interactive Server) for the dashboard

**Status:** Accepted

## Context
The dashboard shows crawl progress, worker status and search results. It is used by a few admins
and viewers, not by the public.

## Decision
Use a Blazor Web App with Interactive Server rendering. It calls the API over HTTP through a
named `HttpClient` ("Api"), configured by `Api__BaseUrl`.

## Rejected alternative
**React SPA.** It would add a second language, an npm toolchain and a separate build, with no gain
for an internal dashboard.

## Consequences
- The whole stack stays C#, and the UI shares DTOs with the API via `Scraper.Contracts`.
- Live updates are simple: a timer re-polls the API and the server pushes the diff over SignalR.
- Each open tab holds a server circuit. That's fine for a few users, but it doesn't scale like a static SPA.
- Web never references Application or Infrastructure; it talks to the API only.
