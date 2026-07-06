using System.Text.Json;
using Jellyfin.Plugin.CsfdBadge.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// JSON-backed cache. The expected data size is small enough to keep in memory.
/// </summary>
public sealed class CsfdCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<CsfdCacheStore> _logger;
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Dictionary<string, CsfdCacheEntry> _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdCacheStore"/> class.
    /// </summary>
    public CsfdCacheStore(IApplicationPaths applicationPaths, ILogger<CsfdCacheStore> logger)
    {
        _logger = logger;
        var directory = Path.Combine(applicationPaths.PluginConfigurationsPath, "CsfdBadge");
        Directory.CreateDirectory(directory);
        _cacheFilePath = Path.Combine(directory, "cache.json");
        _entries = Load();
    }

    /// <summary>
    /// Gets a cached entry.
    /// </summary>
    public CsfdCacheEntry? Get(Guid itemId)
    {
        lock (_entries)
        {
            return _entries.GetValueOrDefault(itemId.ToString("N"));
        }
    }

    /// <summary>
    /// Stores an entry and persists the cache atomically.
    /// </summary>
    public async Task SetAsync(CsfdCacheEntry entry, CancellationToken cancellationToken)
    {
        Dictionary<string, CsfdCacheEntry> snapshot;
        lock (_entries)
        {
            _entries[entry.JellyfinItemId] = entry;
            snapshot = new Dictionary<string, CsfdCacheEntry>(_entries, StringComparer.OrdinalIgnoreCase);
        }

        await PersistAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one cached match.
    /// </summary>
    public async Task DeleteAsync(Guid itemId, CancellationToken cancellationToken)
    {
        Dictionary<string, CsfdCacheEntry> snapshot;
        lock (_entries)
        {
            _entries.Remove(itemId.ToString("N"));
            snapshot = new Dictionary<string, CsfdCacheEntry>(_entries, StringComparer.OrdinalIgnoreCase);
        }

        await PersistAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistAsync(
        Dictionary<string, CsfdCacheEntry> snapshot,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporaryPath = _cacheFilePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _cacheFilePath, true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private Dictionary<string, CsfdCacheEntry> Load()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return new Dictionary<string, CsfdCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, CsfdCacheEntry>>(json, SerializerOptions)
                ?? new Dictionary<string, CsfdCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Could not read ČSFD cache from {CachePath}", _cacheFilePath);
            return new Dictionary<string, CsfdCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
