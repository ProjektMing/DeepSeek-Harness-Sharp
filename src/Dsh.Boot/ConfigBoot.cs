using Dsh.Runtime;
using Dsh.Boot.Profiles;
using Dsh.Runtime.Composition;
using Dsh.Plugins;

namespace Dsh.Boot;

public static class ConfigBoot
{
    public static async Task<HarnessApp> ComposeProfile(
        string profileName,
        IReadOnlyList<Dictionary<string, object?>>? patches,
        HarnessOptions options)
    {
        var (configPath, combinedPatches) = PrepareProfile(options.Home, profileName, patches);
        return await Compose(configPath, options, combinedPatches);
    }

    private static string ProfileConfigTemplate(string profileName)
    {
        var templateName = profileName is "sdk-minimal" or "sdk" ? "minimal" : "standard";
        var path = Path.Combine(AppContext.BaseDirectory, "Profiles", "Templates", $"{templateName}.yaml");
        var template = File.Exists(path) ? File.ReadAllText(path) : "[]\n";
        var entrypoint = profileName switch
        {
            "tui" => "@deepseek-ai/dsh-tui",
            "gui" => "@deepseek-ai/dsh-gui",
            "web" => "@deepseek-ai/dsh-web",
            "acp" => "@deepseek-ai/dsh-acp",
            "lsp" => "@deepseek-ai/dsh-lsp",
            _ => null,
        };
        if (entrypoint is not null && !template.Contains(entrypoint, StringComparison.Ordinal))
        {
            template = template.TrimEnd() + $"\n- name: \"{entrypoint}\"\n";
        }

        return template;
    }

    public static (string ConfigPath, IReadOnlyList<Dictionary<string, object?>>? Patches) PrepareProfile(
        HarnessHome home,
        string profileName,
        IReadOnlyList<Dictionary<string, object?>>? patches)
    {
        ProfileStore.InitProfile(home, profileName);
        var profileDir = ProfileStore.ResolveProfileDir(home, profileName);
        var combined = new List<Dictionary<string, object?>>();
        AddPatches(Path.Combine(profileDir, "cordis.patch.yml"));
        AddPatches(Path.Combine(home.Root, "cordis.patch.yml"));
        if (patches is { Count: > 0 })
            combined.AddRange(patches);

        var configPath = Path.Combine(profileDir, "cordis.yml");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, ProfileConfigTemplate(profileName));
        return (configPath, combined.Count == 0 ? null : combined);

        void AddPatches(string path)
        {
            if (File.Exists(path))
                combined.AddRange(LoadPatches(path));
        }
    }

    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        "Dsh.Interaction.ProviderBootstrapper",
        "Dsh.Interaction")]
    public static async Task<HarnessApp> Compose(string configPath, HarnessOptions options, IReadOnlyList<Dictionary<string, object?>>? patches = null)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new RuntimeException("CONFIG_NOT_FOUND", $"config file not found: {fullPath}");
        await Task.Yield();

        options.Home.Ensure();
        var credentials = new EnvCredentials(options.Home, options.Cwd);
        var ctx = new Context();
        ctx.SetOwn("dshHomePath", options.Home.Root);
        ctx.SetOwn("credentials", credentials);
        ctx.SetOwn("harnessOptions", options);

        var pluginHost = new PluginHost();
        pluginHost.ScanDirectory(AppContext.BaseDirectory);
        ctx.SetOwn("pluginCatalog", pluginHost.Catalog);

        var settings = HarnessSettings.Load(options.Home);
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

        try
        {
            var composition = Composition.Load(
                ctx,
                fullPath,
                builtins: null,
                name => pluginHost.Catalog.TryCreateDefinition(name, out var definition) ? definition : null,
                patches);
            app.Composition = composition;
            var providerRegistration = RegisterProviderAdapters(ctx, options);
            if (providerRegistration is not null)
                app.Track(providerRegistration);
            var manager = new HarnessPluginManager(pluginHost, composition);
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

    public static IReadOnlyList<Dictionary<string, object?>> LoadPatches(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new RuntimeException("PATCHES_NOT_FOUND", $"patch file not found: {fullPath}");
        var list = YamlConfig.Load(File.ReadAllText(fullPath));
        return list.OfType<Dictionary<string, object?>>().ToList();
    }
}
