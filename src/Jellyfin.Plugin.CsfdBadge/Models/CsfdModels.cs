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
}
