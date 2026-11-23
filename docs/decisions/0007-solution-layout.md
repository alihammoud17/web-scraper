# ADR 0007: Solution layout (three changes to the original plan)

**Status:** Accepted

## Context
The original layout was a Clean Architecture solution (Domain, Application, Infrastructure, hosts).
Three problems showed up during planning, and each one is fixed by a small change.

## Decision
1. **`.slnx` solution format.** It's the .NET 10 default: short, readable XML instead of GUID-heavy
   `.sln`, so diffs are reviewable.
2. **`Scraper.Infrastructure.Browser`** holds Playwright, separate from `Scraper.Infrastructure`.
   Only the Worker references it, so the API image doesn't carry Playwright.
3. **`Scraper.Contracts`**: DTOs only, with no dependencies, shared by the API and Web. Web never
   references Application, so the UI can't reach into use cases or infrastructure.

## Rejected alternative
**Keep the original layout** (one Infrastructure project, DTOs inside Application). Every host
would pull in Playwright, and Web would depend on Application just to share DTOs.

## Consequences
- Two more projects, each with one clear reason to exist.
- The dependency rule is enforced by NetArchTest tests in `Scraper.Application.Tests`, so it can't
  silently erode.
- Older tooling that only understands `.sln` won't open the solution. The .NET 10 SDK, VS 2026 and
  Rider support `.slnx`.
