using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Tasks;

/// <summary>
/// Registers the embedded web component with JavaScript Injector after startup.
/// </summary>
public sealed class RegisterWebScriptTask : IScheduledTask
{
    private const string ScriptResourceName = "Jellyfin.Plugin.CsfdBadge.Web.csfd-badge.js";
    private readonly ILogger<RegisterWebScriptTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterWebScriptTask"/> class.
    /// </summary>
    public RegisterWebScriptTask(ILogger<RegisterWebScriptTask> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Register ČSFD web badge";

    /// <inheritdoc />
    public string Key => "CsfdBadgeRegisterWebScript";

    /// <inheritdoc />
    public string Description => "Registers the ČSFD rating component with JavaScript Injector.";

    /// <inheritdoc />
    public string Category => "ČSFD Badge";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin instance is unavailable.");
        var injectorAssembly = AssemblyLoadContext.All
            .SelectMany(static context => context.Assemblies)
            .FirstOrDefault(static assembly =>
                assembly.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector", StringComparison.Ordinal) == true);

        if (injectorAssembly is null)
        {
            _logger.LogWarning(
                "JavaScript Injector was not found. Install it and run this scheduled task again.");
            return Task.CompletedTask;
        }

        var interfaceType = injectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
        var registerMethod = interfaceType?.GetMethod("RegisterScript", BindingFlags.Public | BindingFlags.Static);
        var payloadType = registerMethod?.GetParameters().SingleOrDefault()?.ParameterType;
        var parseMethod = payloadType?.GetMethod(
            "Parse",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

        if (registerMethod is null || parseMethod is null)
        {
            _logger.LogWarning("JavaScript Injector plugin interface is incompatible.");
            return Task.CompletedTask;
        }

        using var stream = GetType().Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ScriptResourceName} was not found.");
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();
        var payloadJson = JsonSerializer.Serialize(new
        {
            id = $"{plugin.Id:N}-badge",
            name = "ČSFD rating badge",
            script,
            enabled = plugin.Configuration.EnableWebBadge,
            requiresAuthentication = true,
            pluginId = plugin.Id.ToString(),
            pluginName = plugin.Name,
            pluginVersion = plugin.Version.ToString()
        });
        var payload = parseMethod.Invoke(null, [payloadJson]);
        var result = registerMethod.Invoke(null, [payload]);

        if (result is true)
        {
            _logger.LogInformation("ČSFD web badge registered successfully.");
        }
        else
        {
            _logger.LogWarning("JavaScript Injector rejected the ČSFD web badge registration.");
        }

        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }
}
