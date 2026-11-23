# ADR 0001: C# / .NET 10 as the language and platform

**Status:** Accepted

## Context
The scraper has an API, distributed workers and a dashboard. The graded parts are the crawling
mechanics themselves: politeness and rate limiting, deduplication, retries and distribution.

## Decision
Use C# 14 on .NET 10 (the current LTS) for every component: an ASP.NET Core minimal API, a Worker
Service and a Blazor dashboard.

## Rejected alternative
**Python + Scrapy.** Scrapy is mature, but its built-in AutoThrottle, duplicate filter and retry
middleware would hide exactly the parts that are graded. Python would also mean a second language
next to a C# UI.

## Consequences
- One typed language and one toolchain across API, worker and UI. DTOs are shared through `Scraper.Contracts`.
- Throttling, the seen set and retries must be written by hand. That's intended, and it's more code.
- Smaller scraping ecosystem than Python. AngleSharp (parsing) and Playwright (rendering) cover what we need.
