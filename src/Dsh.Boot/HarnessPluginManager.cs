using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Dsh.Plugins;
using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Boot;

public sealed class HarnessPluginManager(PluginHost host, Composition composition) : IPluginManager, IDisposable
{
    private readonly Dictionary<string, AssemblyLoadContext> _loadedContexts = new(StringComparer.Ordinal);

    public IReadOnlyList<string> PackageNames => host.Catalog.PackageNames
        .Distinct()
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    public bool SupportsDynamicLoad => RuntimeFeature.IsDynamicCodeSupported;

    public async Task<string> AddAsync(string packageOrPath)
    {
        var spec = packageOrPath.Trim();
        if (spec.Length == 0)
            return "usage: /plugins add <package|path>";
        if (spec.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || File.Exists(spec))
            return await AddFromPathAsync(spec);
        if (host.Catalog.PackageNames.Contains(spec, StringComparer.Ordinal))
            return await ActivateAsync(spec);
        return $"plugin package not found: {spec}";
    }

    private async Task<string> AddFromPathAsync(string spec)
    {
        var path = Path.GetFullPath(spec);
        if (!File.Exists(path))
            return $"plugin file not found: {path}";
        try
        {
            var context = new AssemblyLoadContext($"dsh-plugin:{Path.GetFileNameWithoutExtension(path)}", isCollectible: true);
            var assembly = context.LoadFromAssemblyPath(path);
            var added = host.Catalog.RegisterAssembly(assembly, context);
            if (added.Count == 0)
            {
                context.Unload();
                return $"assembly has no DSH plugin: {path}";
            }
            var messages = new List<string>();
            foreach (var package in added)
            {
                _loadedContexts[package] = context;
                messages.Add(await ActivateAsync(package));
            }
            return string.Join('\n', messages);
        }
        catch (PlatformNotSupportedException)
        {
            return "dynamic plugin loading is not supported in this build (NativeAOT); edit the profile config and restart";
        }
    }

    private async Task<string> ActivateAsync(string package)
    {
        if (composition.Find(package) is { } existing)
            return $"plugin {package} is already active ({existing.State})";
        if (!host.Catalog.TryCreateDefinition(package, out var definition))
            return $"plugin package not found: {package}";
        var activation = await composition.AddAsync(definition!);
        return activation.State == ActivationState.Active
            ? $"plugin {package} activated"
            : $"plugin {package} failed to activate: {activation.Error?.GetType().Name}";
    }

    public async Task<string> RemoveAsync(string package)
    {
        var trimmed = package.Trim();
        if (trimmed.Length == 0)
            return "usage: /plugins remove <package>";
        var activation = composition.Find(trimmed);
        if (activation is null)
            return $"plugin {trimmed} is not active";
        await activation.DeactivateAsync();
        if (_loadedContexts.Remove(trimmed, out var context) && context.IsCollectible)
        {
            host.Catalog.Remove(trimmed);
            context.Unload();
        }
        return $"plugin {trimmed} removed";
    }

    public void Dispose()
    {
        foreach (var context in _loadedContexts.Values.Distinct())
        {
            if (context.IsCollectible)
                context.Unload();
        }
        _loadedContexts.Clear();
    }
}
