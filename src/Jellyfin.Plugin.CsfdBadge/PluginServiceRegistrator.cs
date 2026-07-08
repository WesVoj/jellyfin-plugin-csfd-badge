using Jellyfin.Plugin.CsfdBadge.Services;
using Jellyfin.Plugin.CsfdBadge.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CsfdBadge;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CsfdApiClient>();
        serviceCollection.AddSingleton<CsfdCacheStore>();
        serviceCollection.AddSingleton<CsfdLookupService>();
        serviceCollection.AddSingleton<CsfdCardFetchQueue>();
        serviceCollection.AddHostedService(static services => services.GetRequiredService<CsfdCardFetchQueue>());
        serviceCollection.AddSingleton<CsfdBackfillService>();
        serviceCollection.AddHostedService(static services => services.GetRequiredService<CsfdBackfillService>());
        serviceCollection.AddSingleton<RegisterWebScriptTask>();
    }
}
