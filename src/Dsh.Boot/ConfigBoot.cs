using Dsh.Runtime;
using Dsh.Runtime.Composition;
using Dsh.Runtime.Logging;
using Dsh.Plugins;
using System.Runtime.CompilerServices;

namespace Dsh.Boot;

public static class ConfigBoot
{
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        "Dsh.Interaction.ProviderBootstrapper",
        "Dsh.Interaction")]
    public static async Task<HarnessApp> Compose(HarnessOptions options)
    {
        await Task.Yield();
        options.Home.Ensure();
        var settings = HarnessSettings.Load(options.Home);
        settings.Plugins = PluginManifest.Merge(PluginManifest.LoadDefaults(), settings.Plugins);
        var logging = LoggingSetup.Create(settings.Logging?.ToOptions(), options.Home.LogsPath, allowConsole: !options.IsTui);
        var credentials = new EnvCredentials(options.Home, options.Cwd);
        var ctx = new Context(logging);
        ctx.SetOwn("dshHomePath", options.Home.Root);
        ctx.SetOwn("credentials", credentials);
        ctx.SetOwn("harnessOptions", options);

        var pluginHost = new PluginHost();
        pluginHost.ScanDirectory(AppContext.BaseDirectory);
        var pluginsDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        var discovered = new List<string>();
        var nativePlugins = new List<INativePlugin>();
        if (Directory.Exists(pluginsDirectory))
        {
            if (RuntimeFeature.IsDynamicCodeSupported)
                discovered.AddRange(pluginHost.ScanPluginDirectory(pluginsDirectory));
            discovered.AddRange(RegisterNativePlugins(ctx, pluginHost, pluginsDirectory, nativePlugins));
            if (!RuntimeFeature.IsDynamicCodeSupported
                && discovered.Count == 0
                && Directory.EnumerateFiles(pluginsDirectory, "*.dll").Any())
                ctx.LoggerFor("loader").Warn("%s",
                    $"NativeAOT build cannot load managed plugins from {pluginsDirectory}; ship native plugins there or compile managed plugins into the image");
        }
        PluginManifest.IncludeDiscovered(settings.Plugins, discovered);
        ctx.SetOwn("pluginCatalog", pluginHost.Catalog);

        var defaultModel = settings.ResolveDefaultModel();
        var provider = options.Provider ?? defaultModel?.Provider ?? HarnessComposer.DefaultProvider;
        var model = options.Model ?? defaultModel?.Model ?? HarnessComposer.DefaultModel;

        var app = new HarnessApp
        {
            Ctx = ctx,
            Home = options.Home,
            Credentials = credentials,
            Provider = provider,
            Model = model,
            ReasoningEffort = options.ReasoningEffort,
        };
        app.Track(logging);
        if (nativePlugins.Count > 0)
            app.Track(new PluginDisposables(nativePlugins));

        try
        {
            var composition = await Composition.StartAsync(ctx, BuildEntries(ctx, pluginHost, options, settings.Plugins));
            app.Composition = composition;
            ctx.LoggerFor("boot").Info("composition ready: %d plugin(s), home %s", composition.Activations.Count, options.Home.Root);
            var providerRegistration = RegisterProviderAdapters(ctx, options);
            if (providerRegistration is not null)
                app.Track(providerRegistration);
            var manager = new HarnessPluginManager(pluginHost, composition, options.Home, settings);
            app.Track(manager);
            ctx.Provide("pluginManager", manager);
        }
        catch
        {
            app.Dispose();
            throw;
        }
        return app;
    }

    private static List<PluginEntry> BuildEntries(
        Context ctx,
        PluginHost host,
        HarnessOptions options,
        IReadOnlyDictionary<string, PluginSetting> plugins)
    {
        var entries = new List<PluginEntry>();
        foreach (var (name, setting) in plugins)
        {
            if (!setting.Enabled)
                continue;
            if (!host.Catalog.TryCreateDefinition(name, out var definition))
            {
                ctx.LoggerFor("loader").Error("%s", $"plugin not found: {name}");
                continue;
            }
            entries.Add(new PluginEntry(definition!, setting.Parameters.Count == 0 ? null : setting.Parameters));
        }
        if (options.EntrypointPlugin is { Length: > 0 } entrypoint
            && !plugins.ContainsKey(entrypoint)
            && host.Catalog.TryCreateDefinition(entrypoint, out var entryDefinition))
        {
            entries.Add(new PluginEntry(entryDefinition!, null));
        }
        return entries;
    }

    private static IDisposable? RegisterProviderAdapters(Context ctx, HarnessOptions options)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Dsh.Interaction.ProviderBootstrapper"))
            .FirstOrDefault(candidate => candidate is not null);
        if (type is null)
        {
            ctx.Logger.Warn("%s", "Dsh.Interaction.ProviderBootstrapper is not available in this build; LLM provider adapters cannot be registered");
            return null;
        }
        return (IDisposable)type.GetMethod("Register", [typeof(Context), typeof(HarnessOptions)])!
            .Invoke(null, [ctx, options])!;
    }

    /** 扫描 plugins 目录中的原生共享库,经桥接程序集转成插件定义。 */
    private static IReadOnlyList<string> RegisterNativePlugins(
        Context ctx,
        PluginHost host,
        string directory,
        List<INativePlugin> loaded)
    {
        var packages = new List<string>();
        var bridge = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Dsh.Plugins.Native.Host.NativePluginBridge"))
            .FirstOrDefault(candidate => candidate is not null);
        foreach (var file in Directory.EnumerateFiles(directory).Where(IsNativeLibrary))
        {
            var plugin = NativePluginLibrary.TryLoad(file);
            if (plugin is null)
                continue;
            if (bridge is null)
            {
                plugin.Dispose();
                ctx.LoggerFor("loader").Warn("%s",
                    $"native plugin \"{plugin.Package}\" loaded from {file} but the native bridge is not available in this build");
                continue;
            }
            var create = bridge.GetMethod("CreateDefinition", [typeof(INativePlugin)])!;
            host.Catalog.RegisterDefinition(plugin.Package, () => (PluginDefinition)create.Invoke(null, [plugin])!);
            loaded.Add(plugin);
            packages.Add(plugin.Package);
            ctx.LoggerFor("loader").Info("%s", $"native plugin registered: {plugin.Package} ({Path.GetFileName(file)})");
        }
        return packages;
    }

    private static bool IsNativeLibrary(string path)
        => Path.GetExtension(path) is ".so" or ".dylib" or ".dll";

    private sealed class PluginDisposables(IReadOnlyList<INativePlugin> plugins) : IDisposable
    {
        public void Dispose()
        {
            foreach (var plugin in plugins)
                plugin.Dispose();
        }
    }
}
