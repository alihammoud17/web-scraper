using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scraper.Application.Crawling;
using Scraper.Domain.Sites;
using Scraper.Infrastructure.Persistence;

namespace Scraper.Infrastructure.Crawling;

/// <summary>The <c>sites</c> table, kept in memory for 5 minutes (it's looked up on every request).</summary>
public sealed class SiteCatalog(IServiceScopeFactory scopes, TimeProvider time) : ISiteCatalog, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private IReadOnlyDictionary<string, SiteConfig> _byAuthority = new Dictionary<string, SiteConfig>();
    private IReadOnlyDictionary<string, SiteConfig> _byId = new Dictionary<string, SiteConfig>();
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<SiteConfig?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() >= _expiresAt)
        {
            await RefreshAsync(cancellationToken);
        }
        return _byAuthority.GetValueOrDefault(authority.ToLowerInvariant());
    }

    public async Task<SiteConfig?> FindByIdAsync(string siteId, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() >= _expiresAt)
        {
            await RefreshAsync(cancellationToken);
        }
        return _byId.GetValueOrDefault(siteId);
    }

    public void Dispose() => _refresh.Dispose();

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (time.GetUtcNow() < _expiresAt)
            {
                return; // another caller refreshed while we waited
            }
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ScraperDbContext>();
            var sites = await database.Sites.AsNoTracking().ToListAsync(cancellationToken);
            _byAuthority = sites.ToDictionary(s => s.BaseUrl.Authority);
            _byId = sites.ToDictionary(s => s.Id);
            _expiresAt = time.GetUtcNow() + CacheDuration;
        }
        finally
        {
            _refresh.Release();
        }
    }
}
