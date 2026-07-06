using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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
                var refreshed = await MatchAsync(item, cancellationToken).ConfigureAwait(false);
                await _cacheStore.SetAsync(refreshed, cancellationToken).ConfigureAwait(false);
                return ToResponse(refreshed, false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
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
            IsStale = isStale
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
                var score = Score(result, item, query, index);
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
            || winner.Score < minimumScore
            || (ordered.Length > 1 && winner.Score - ordered[1].Score < 5 && winner.Score < 100))
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

        var verifiedScore = Math.Max(winner.Score, ScoreDetail(detail, item));
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

    private static int Score(CsfdSearchItem candidate, BaseItem item, string query, int resultIndex)
    {
        var score = 20;
        var itemYear = item.ProductionYear;
        if (itemYear.HasValue && candidate.Year == itemYear.Value)
        {
            score += 30;
        }
        else if (itemYear.HasValue && Math.Abs(candidate.Year - itemYear.Value) == 1)
        {
            score += 12;
        }
        else if (itemYear.HasValue && candidate.Year > 0)
        {
            // Remakes and unrelated films often share an exact title. A clearly
            // different year must outweigh search-result order and title score.
            score -= 50;
        }

        score += TitleScore(candidate.Title, item.Name, item.OriginalTitle);
        if (resultIndex == 0)
        {
            score += 20;
        }
        else if (resultIndex < 3)
        {
            score += 10;
        }

        if (Normalize(candidate.Title) == Normalize(query))
        {
            score += 10;
        }

        return score;
    }

    private static int ScoreDetail(CsfdMovieDetail detail, BaseItem item)
    {
        var score = 20;
        if (item.ProductionYear.HasValue && detail.Year == item.ProductionYear.Value)
        {
            score += 30;
        }
        else if (item.ProductionYear.HasValue && detail.Year > 0
                 && Math.Abs(detail.Year - item.ProductionYear.Value) > 1)
        {
            score -= 50;
        }

        var titles = detail.TitlesOther.Select(static title => title.Title).Append(detail.Title);
        var bestTitleScore = titles.Max(title => TitleScore(title, item.Name, item.OriginalTitle));
        return score + bestTitleScore;
    }

    private static int TitleScore(string candidate, string name, string? originalTitle)
    {
        var normalizedCandidate = Normalize(candidate);
        var itemTitles = new[] { name, originalTitle }
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => Normalize(title!));

        var best = 0;
        foreach (var title in itemTitles)
        {
            if (normalizedCandidate == title)
            {
                best = Math.Max(best, 50);
            }
            else if (normalizedCandidate.Contains(title, StringComparison.Ordinal)
                     || title.Contains(normalizedCandidate, StringComparison.Ordinal))
            {
                best = Math.Max(best, 30);
            }
            else
            {
                best = Math.Max(best, (int)Math.Round(TokenSimilarity(normalizedCandidate, title) * 25));
            }
        }

        return best;
    }

    private static double TokenSimilarity(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return (double)intersection / union;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
