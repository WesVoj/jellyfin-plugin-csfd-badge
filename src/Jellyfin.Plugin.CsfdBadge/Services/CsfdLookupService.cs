using System.Collections.Concurrent;
using Jellyfin.Plugin.CsfdBadge.Models;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Matches Jellyfin items with ČSFD records and maintains the cache.
/// </summary>
public sealed class CsfdLookupService
{
    private readonly CsfdApiClient _apiClient;
    private readonly CsfdCacheStore _cacheStore;
    private readonly ILogger<CsfdLookupService> _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _itemLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdLookupService"/> class.
    /// </summary>
    public CsfdLookupService(
        CsfdApiClient apiClient,
        CsfdCacheStore cacheStore,
        ILogger<CsfdLookupService> logger)
    {
        _apiClient = apiClient;
        _cacheStore = cacheStore;
        _logger = logger;
    }

    /// <summary>
    /// Gets a fresh or cached badge for a Jellyfin item.
    /// </summary>
    public async Task<CsfdBadgeResponse?> GetBadgeAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var itemId = item.Id;
        var cached = _cacheStore.Get(itemId);
        if (IsFresh(cached))
        {
            return ToResponse(cached!, false);
        }

        var itemLock = _itemLocks.GetOrAdd(itemId, static _ => new SemaphoreSlim(1, 1));
        await itemLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _cacheStore.Get(itemId);
            if (IsFresh(cached))
            {
                return ToResponse(cached!, false);
            }

            try
            {
                var refreshed = cached is { IsManualMatch: true, CsfdId: not null }
                    ? await LoadManualMatchAsync(item, cached.CsfdId.Value, cancellationToken).ConfigureAwait(false)
                    : await MatchAsync(item, cancellationToken).ConfigureAwait(false);
                await _cacheStore.SetAsync(refreshed, cancellationToken).ConfigureAwait(false);
                return ToResponse(refreshed, false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                if (cached is { NoMatch: false, Rating: not null, CsfdId: not null, Url: not null })
                {
                    _logger.LogWarning(
                        exception,
                        "ČSFD is unavailable; returning stale cache for item {ItemId}",
                        item.Id);
                    return ToResponse(cached, true);
                }

                throw;
            }
        }
        finally
        {
            itemLock.Release();
        }
    }

    /// <summary>
    /// Stores an administrator-selected ČSFD match.
    /// </summary>
    public async Task<CsfdBadgeResponse> SetManualMatchAsync(
        BaseItem item,
        int csfdId,
        CancellationToken cancellationToken)
    {
        if (csfdId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(csfdId), "ČSFD ID must be positive.");
        }

        var itemLock = _itemLocks.GetOrAdd(item.Id, static _ => new SemaphoreSlim(1, 1));
        await itemLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = await LoadManualMatchAsync(item, csfdId, cancellationToken).ConfigureAwait(false);
            await _cacheStore.SetAsync(entry, cancellationToken).ConfigureAwait(false);
            return ToResponse(entry, false)
                ?? throw new InvalidOperationException("The selected ČSFD title has no published rating.");
        }
        finally
        {
            itemLock.Release();
        }
    }

    /// <summary>
    /// Removes an administrator-selected match so automatic matching can run again.
    /// </summary>
    public async Task ClearManualMatchAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var itemLock = _itemLocks.GetOrAdd(item.Id, static _ => new SemaphoreSlim(1, 1));
        await itemLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cacheStore.Get(item.Id)?.IsManualMatch == true)
            {
                await _cacheStore.DeleteAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            itemLock.Release();
        }
    }

    private static CsfdBadgeResponse? ToResponse(CsfdCacheEntry entry, bool isStale)
    {
        if (entry.NoMatch
            || entry.CsfdId is null
            || entry.Rating is null
            || string.IsNullOrWhiteSpace(entry.Url))
        {
            return null;
        }

        return new CsfdBadgeResponse
        {
            CsfdId = entry.CsfdId.Value,
            Rating = entry.Rating.Value,
            RatingCount = entry.RatingCount,
            Url = entry.Url,
            Title = entry.CsfdTitle ?? entry.ItemTitle,
            MatchScore = entry.MatchScore,
            IsStale = isStale,
            IsManualMatch = entry.IsManualMatch
        };
    }

    private static bool IsFresh(CsfdCacheEntry? entry)
    {
        if (entry is null)
        {
            return false;
        }

        var configuration = Plugin.Instance?.Configuration;
        var hours = entry.NoMatch
            ? configuration?.NegativeCacheHours ?? 24
            : configuration?.CacheHours ?? 168;
        return entry.FetchedAtUtc >= DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 8760));
    }

    private async Task<CsfdCacheEntry> MatchAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var itemType = item.GetType().Name;
        var isSeries = string.Equals(itemType, "Series", StringComparison.Ordinal);
        var queries = new[] { item.OriginalTitle, item.Name }
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => title!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidates = new Dictionary<int, ScoredCandidate>();
        foreach (var query in queries)
        {
            var search = await _apiClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var results = isSeries ? search?.TvSeries : search?.Movies;
            if (results is null)
            {
                continue;
            }

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                var score = CsfdMatcher.ScoreSearchResult(
                    result,
                    item.Name,
                    item.OriginalTitle,
                    item.ProductionYear,
                    query,
                    index);
                if (!candidates.TryGetValue(result.Id, out var existing) || score > existing.Score)
                {
                    candidates[result.Id] = new ScoredCandidate(result, score);
                }
            }
        }

        var ordered = candidates.Values.OrderByDescending(static candidate => candidate.Score).ToArray();
        var minimumScore = Math.Clamp(Plugin.Instance?.Configuration.MinimumMatchScore ?? 70, 40, 120);
        var winner = ordered.FirstOrDefault();

        if (winner is null
            || !CsfdMatcher.IsSafeWinner(
                ordered.Select(static candidate => candidate.Score).ToArray(),
                minimumScore))
        {
            _logger.LogInformation(
                "No safe ČSFD match for {ItemName} ({Year}); best score was {Score}",
                item.Name,
                item.ProductionYear,
                winner?.Score ?? 0);
            return CreateNoMatch(item, itemType);
        }

        var detail = await _apiClient.GetMovieAsync(winner.Item.Id, cancellationToken).ConfigureAwait(false);
        if (detail?.Rating is null || !IsCsfdUrl(detail.Url))
        {
            return CreateNoMatch(item, itemType);
        }

        var verifiedScore = Math.Max(
            winner.Score,
            CsfdMatcher.ScoreDetail(detail, item.Name, item.OriginalTitle, item.ProductionYear));
        if (verifiedScore < minimumScore)
        {
            return CreateNoMatch(item, itemType);
        }

        _logger.LogInformation(
            "Matched {ItemName} ({Year}) to ČSFD {CsfdId} {CsfdTitle} with score {Score}",
            item.Name,
            item.ProductionYear,
            detail.Id,
            detail.Title,
            verifiedScore);

        return new CsfdCacheEntry
        {
            JellyfinItemId = item.Id.ToString("N"),
            ItemTitle = item.Name,
            ItemOriginalTitle = item.OriginalTitle,
            ItemYear = item.ProductionYear,
            ItemType = itemType,
            CsfdId = detail.Id,
            CsfdTitle = detail.Title,
            Rating = detail.Rating,
            RatingCount = detail.RatingCount,
            Url = detail.Url,
            MatchScore = verifiedScore,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            NoMatch = false
        };
    }

    private async Task<CsfdCacheEntry> LoadManualMatchAsync(
        BaseItem item,
        int csfdId,
        CancellationToken cancellationToken)
    {
        var detail = await _apiClient.GetMovieAsync(csfdId, cancellationToken).ConfigureAwait(false);
        if (detail is null || detail.Id != csfdId || detail.Rating is null || !IsCsfdUrl(detail.Url))
        {
            throw new InvalidOperationException(
                "The selected ČSFD ID is invalid, unavailable, or does not have a published rating.");
        }

        _logger.LogInformation(
            "Manually matched {ItemName} ({Year}) to ČSFD {CsfdId} {CsfdTitle}",
            item.Name,
            item.ProductionYear,
            detail.Id,
            detail.Title);

        return new CsfdCacheEntry
        {
            JellyfinItemId = item.Id.ToString("N"),
            ItemTitle = item.Name,
            ItemOriginalTitle = item.OriginalTitle,
            ItemYear = item.ProductionYear,
            ItemType = item.GetType().Name,
            CsfdId = detail.Id,
            CsfdTitle = detail.Title,
            Rating = detail.Rating,
            RatingCount = detail.RatingCount,
            Url = detail.Url,
            MatchScore = 120,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            NoMatch = false,
            IsManualMatch = true
        };
    }

    private static bool IsCsfdUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && (uri.Host.Equals("csfd.cz", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith(".csfd.cz", StringComparison.OrdinalIgnoreCase));
    }

    private static CsfdCacheEntry CreateNoMatch(BaseItem item, string itemType)
    {
        return new CsfdCacheEntry
        {
            JellyfinItemId = item.Id.ToString("N"),
            ItemTitle = item.Name,
            ItemOriginalTitle = item.OriginalTitle,
            ItemYear = item.ProductionYear,
            ItemType = itemType,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            NoMatch = true
        };
    }

    private sealed record ScoredCandidate(CsfdSearchItem Item, int Score);
}
