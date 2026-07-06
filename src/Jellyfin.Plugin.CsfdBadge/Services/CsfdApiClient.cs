using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.CsfdBadge.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Rate-limited HTTP client for node-csfd-api.
/// </summary>
public sealed class CsfdApiClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CsfdApiClient> _logger;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdApiClient"/> class.
    /// </summary>
    public CsfdApiClient(ILogger<CsfdApiClient> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-CsfdBadge/0.1");
    }

    /// <summary>
    /// Searches ČSFD.
    /// </summary>
    public Task<CsfdSearchResponse?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return GetAsync<CsfdSearchResponse>(
            $"search/{Uri.EscapeDataString(query)}",
            cancellationToken);
    }

    /// <summary>
    /// Loads one ČSFD item.
    /// </summary>
    public Task<CsfdMovieDetail?> GetMovieAsync(int id, CancellationToken cancellationToken)
    {
        return GetAsync<CsfdMovieDetail>($"movie/{id}", cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
        _requestLock.Dispose();
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
        var baseUrl = configuration.ApiBaseUrl?.Trim().TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("node-csfd-api URL is invalid.");
        }

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configuredDelay = Math.Clamp(configuration.RequestDelayMilliseconds, 500, 30000);
            var remainingDelay = TimeSpan.FromMilliseconds(configuredDelay)
                - (DateTimeOffset.UtcNow - _lastRequestUtc);
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
            }

            var requestUri = new Uri(baseUri, $"{baseUri.AbsolutePath.TrimEnd('/')}/{relativePath}");
            _logger.LogDebug("Requesting ČSFD bridge endpoint {RequestUri}", requestUri);

            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            _lastRequestUtc = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }
    }
}
