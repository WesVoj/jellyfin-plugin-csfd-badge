using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CsfdBadge.Configuration;

/// <summary>
/// User-editable plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the internal node-csfd-api URL.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://csfd-api:3000";

    /// <summary>
    /// Gets or sets successful rating cache duration in hours.
    /// </summary>
    public int CacheHours { get; set; } = 168;

    /// <summary>
    /// Gets or sets unsuccessful match cache duration in hours.
    /// </summary>
    public int NegativeCacheHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets the minimum automatic match score.
    /// </summary>
    public int MinimumMatchScore { get; set; } = 70;

    /// <summary>
    /// Gets or sets the minimum delay between scraper requests.
    /// </summary>
    public int RequestDelayMilliseconds { get; set; } = 1200;

    /// <summary>
    /// Gets or sets a value indicating whether the web badge is enabled.
    /// </summary>
    public bool EnableWebBadge { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether cached ratings are shown on library cards.
    /// </summary>
    public bool EnableLibraryCardBadges { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether visible uncached cards are queued for lookup.
    /// </summary>
    public bool FetchCardRatingsWhileBrowsing { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pending card lookups.
    /// </summary>
    public int CardFetchQueueLimit { get; set; } = 50;
}
