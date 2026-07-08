using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CsfdBadge.Models;

/// <summary>
/// Search response returned by node-csfd-api.
/// </summary>
public sealed class CsfdSearchResponse
{
    /// <summary>Gets or sets movie results.</summary>
    public IReadOnlyList<CsfdSearchItem> Movies { get; set; } = [];

    /// <summary>Gets or sets series results.</summary>
    public IReadOnlyList<CsfdSearchItem> TvSeries { get; set; } = [];
}

/// <summary>
/// One ČSFD search result.
/// </summary>
public sealed class CsfdSearchItem
{
    /// <summary>Gets or sets the ČSFD identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets release year.</summary>
    public int Year { get; set; }

    /// <summary>Gets or sets content type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets URL.</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Movie detail returned by node-csfd-api.
/// </summary>
public sealed class CsfdMovieDetail
{
    /// <summary>Gets or sets the ČSFD identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets release year.</summary>
    public int Year { get; set; }

    /// <summary>Gets or sets URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets rating percentage.</summary>
    public int? Rating { get; set; }

    /// <summary>Gets or sets rating count.</summary>
    public int? RatingCount { get; set; }

    /// <summary>Gets or sets alternative titles.</summary>
    public IReadOnlyList<CsfdAlternativeTitle> TitlesOther { get; set; } = [];
}

/// <summary>
/// Alternative localized title.
/// </summary>
public sealed class CsfdAlternativeTitle
{
    /// <summary>Gets or sets country.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Gets or sets title.</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Persistent cached match.
/// </summary>
public sealed class CsfdCacheEntry
{
    /// <summary>Gets or sets the Jellyfin item identifier.</summary>
    public string JellyfinItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin title used for matching.</summary>
    public string ItemTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the original title used for matching.</summary>
    public string? ItemOriginalTitle { get; set; }

    /// <summary>Gets or sets the Jellyfin production year.</summary>
    public int? ItemYear { get; set; }

    /// <summary>Gets or sets the Jellyfin item type.</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the ČSFD identifier.</summary>
    public int? CsfdId { get; set; }

    /// <summary>Gets or sets matched ČSFD title.</summary>
    public string? CsfdTitle { get; set; }

    /// <summary>Gets or sets rating percentage.</summary>
    public int? Rating { get; set; }

    /// <summary>Gets or sets rating count.</summary>
    public int? RatingCount { get; set; }

    /// <summary>Gets or sets direct ČSFD URL.</summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets automatic match score.</summary>
    public int MatchScore { get; set; }

    /// <summary>Gets or sets the time at which the record was fetched.</summary>
    public DateTimeOffset FetchedAtUtc { get; set; }

    /// <summary>Gets or sets a value indicating whether no safe match was found.</summary>
    public bool NoMatch { get; set; }

    /// <summary>Gets or sets a value indicating whether an administrator selected the match.</summary>
    public bool IsManualMatch { get; set; }
}

/// <summary>
/// Public badge response consumed by the injected web component.
/// </summary>
public sealed class CsfdBadgeResponse
{
    /// <summary>Gets or sets ČSFD ID.</summary>
    public int CsfdId { get; set; }

    /// <summary>Gets or sets rating percentage.</summary>
    public int Rating { get; set; }

    /// <summary>Gets or sets rating count.</summary>
    public int? RatingCount { get; set; }

    /// <summary>Gets or sets direct URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets matched title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets match score.</summary>
    public int MatchScore { get; set; }

    /// <summary>Gets or sets a value indicating whether stale cache was returned.</summary>
    public bool IsStale { get; set; }

    /// <summary>Gets or sets a value indicating whether an administrator selected the match.</summary>
    public bool IsManualMatch { get; set; }
}

/// <summary>
/// Administrator request for explicitly pairing a Jellyfin item with ČSFD.
/// </summary>
public sealed class CsfdManualMatchRequest
{
    /// <summary>Gets or sets the ČSFD identifier.</summary>
    public int CsfdId { get; set; }
}

/// <summary>
/// Batch request made by the library card component.
/// </summary>
public sealed class CsfdBadgeBatchRequest
{
    /// <summary>Gets or sets Jellyfin item identifiers.</summary>
    public IReadOnlyList<string> ItemIds { get; set; } = [];
}

/// <summary>
/// Cached ratings returned for a batch of library cards.
/// </summary>
public sealed class CsfdBadgeBatchResponse
{
    /// <summary>Gets or sets cached ratings keyed by the requested Jellyfin item ID.</summary>
    public Dictionary<string, CsfdBadgeResponse> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the current number of queued lookups.</summary>
    public int QueueSize { get; set; }

    /// <summary>Gets or sets requested item IDs whose ratings are still pending.</summary>
    public List<string> PendingItemIds { get; set; } = [];
}

/// <summary>
/// Public web-component settings.
/// </summary>
public sealed class CsfdBadgeClientConfiguration
{
    /// <summary>Gets or sets a value indicating whether card badges are enabled.</summary>
    public bool EnableLibraryCardBadges { get; set; }

    /// <summary>Gets or sets a value indicating whether uncached visible cards may be queued.</summary>
    public bool FetchCardRatingsWhileBrowsing { get; set; }
}

/// <summary>
/// Administrative queue and backfill status.
/// </summary>
public sealed class CsfdAdminStatusResponse
{
    /// <summary>Gets or sets the current backfill state.</summary>
    public string State { get; set; } = "Idle";

    /// <summary>Gets or sets the total number of movies and series in the library.</summary>
    public int LibraryItems { get; set; }

    /// <summary>Gets or sets the number of items selected for the current backfill.</summary>
    public int Total { get; set; }

    /// <summary>Gets or sets the number of items completed in the current backfill.</summary>
    public int Processed { get; set; }

    /// <summary>Gets or sets the number of items remaining in the current backfill.</summary>
    public int Remaining { get; set; }

    /// <summary>Gets or sets the number of successful rating matches.</summary>
    public int Succeeded { get; set; }

    /// <summary>Gets or sets the number of completed items without a safe rating match.</summary>
    public int NotFound { get; set; }

    /// <summary>Gets or sets the number of failed lookups.</summary>
    public int Failed { get; set; }

    /// <summary>Gets or sets the number of fresh cache entries skipped at startup.</summary>
    public int Skipped { get; set; }

    /// <summary>Gets or sets the completion percentage from zero to one hundred.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Gets or sets the title currently being processed.</summary>
    public string? CurrentTitle { get; set; }

    /// <summary>Gets or sets the latest backfill error.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the time at which the current run started.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>Gets or sets the time at which the current run ended.</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>Gets or sets the current lazy card queue size.</summary>
    public int LazyQueueSize { get; set; }

    /// <summary>Gets or sets the configured lazy card queue capacity.</summary>
    public int LazyQueueLimit { get; set; }
}
