# ADR 0005: Playwright for JS-rendered pages

**Status:** Accepted

## Context
Rockstar Newswire is a single-page app: the raw HTML has no articles until JavaScript runs. The
other two sites are server-rendered and don't need a browser.

## Decision
Use **Microsoft.Playwright** in the worker only, isolated in `Scraper.Infrastructure.Browser`.
Only sites configured as JS-rendered go through the browser (here, only Rockstar Newswire).
- **One browser context per worker**, reused across pages, so shared assets stay cached.
- **Block images, fonts and media** through request routing: they aren't content, and blocking them
  saves bandwidth for us and the site.
- **Wait for a content selector** (e.g. the article body), not "network idle", which is slow and
  unreliable on pages with analytics or long-polling.
- **Hash the cleaned blocks, not the rendered HTML**, since rendered markup changes every load (ADR 0009).

## Rejected alternatives
- **Selenium:** needs a separate driver, has no auto-waiting, and gives a weaker .NET experience.
- **PuppeteerSharp:** a community port, Chromium-focused, with no official Docker image.

## Consequences
- First-party .NET API, auto-waiting locators, and an official Docker image with browsers preinstalled.
- The worker image is large (~2 GB), but the API image stays small because it never references Playwright.
- The Docker image tag must equal the `Microsoft.Playwright` package version.
