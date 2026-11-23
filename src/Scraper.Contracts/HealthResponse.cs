namespace Scraper.Contracts;

/// <summary>Body of <c>GET /health</c>. Each dependency is "healthy" or "unhealthy".</summary>
public sealed record HealthResponse(string Status, string Postgres, string Redis, string Rabbitmq, int ActiveWorkers);
