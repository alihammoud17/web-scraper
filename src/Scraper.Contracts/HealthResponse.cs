namespace Scraper.Contracts;

/// <summary>
/// Body of <c>GET /health</c>. Each dependency is "healthy" or "unhealthy". <see cref="QueueLength"/> is the number of
/// tasks waiting in the work queue (null when it can't be read, e.g. before any worker declared the queue).
/// </summary>
public sealed record HealthResponse(string Status, string Postgres, string Redis, string Rabbitmq, int ActiveWorkers, long? QueueLength);
