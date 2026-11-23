# Distributed Web Scraper

A distributed crawler for **GTA V vehicles and updates**, built on .NET 10. It crawls three sites:

- **GTACars.net**: mostly static HTML.
- **Rockstar Newswire**: JS-rendered, so it goes through Playwright.
- **GTA5-Mods.com**: paginated, 500+ pages.

Workers share a RabbitMQ queue and Redis state, results go into PostgreSQL, and a Blazor dashboard
shows them. This is Phase 1, Milestone 2: the skeleton (health endpoint, migrations, worker heartbeat).

## Prerequisites

- Docker with Compose v2
- .NET SDK 10.0.302 or a later 10.0 feature band, to build and test outside Docker (see `global.json`)

## Run

```bash
cp .env.example .env      # then replace the placeholder values
docker compose up --build
```

The `migrator` service applies database migrations first. `api` and `worker` start only after it exits successfully.

## Scale the workers

```bash
docker compose up -d --scale worker=4
curl localhost:8080/health   # "activeWorkers": 4 within a few seconds
```

## URLs

| Service | URL |
|---|---|
| API health | http://localhost:8080/health |
| API reference (Scalar) | http://localhost:8080/scalar |
| OpenAPI document | http://localhost:8080/openapi/v1.json |
| Web dashboard | http://localhost:8081 |
| RabbitMQ management | http://localhost:15672 (credentials from `.env`) |
| Test site | http://localhost:8090 |

## Tests and checks

```bash
dotnet build
dotnet test                          # includes the architecture tests
dotnet format --verify-no-changes
```

Stop everything and delete the data with `docker compose down -v`.

## Docs

- `docs/decisions/`: architecture decision records (ADRs)
- `docs/compliance/sites.md`: robots.txt, terms and rate limits per site
