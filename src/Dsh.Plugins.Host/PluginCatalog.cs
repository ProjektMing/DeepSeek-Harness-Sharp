using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Dsh.Runtime;

namespace Dsh.Plugins;

public sealed class PluginCatalog
{
    private readonly Dictionary<string, PluginTypeHolder> _plugins = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> PackageNames => _plugins.Keys;

    public IReadOnlyList<(string Package, Type Implementation)> Enumerate()
        => _plugins.Select(entry => (entry.Key, entry.Value.Type)).ToList();

    public IReadOnlyList<string> RegisterAssembly(Assembly assembly, AssemblyLoadContext? context = null)
    {
        var attributes = assembly.GetCustomAttributes<DshPluginAttribute>().ToList();
        if (attributes.Count == 0)
            return [];
        var pluginType = FindPluginType(assembly);
        var registered = new List<string>();
        foreach (var attribute in attributes)
        {
            if (_plugins.TryGetValue(attribute.PackageName, out var existing))
            {
                throw new InvalidOperationException(
                    $"Plugin package '{attribute.PackageName}' is already registered by {existing.Type.FullName}; cannot also register {pluginType.FullName}.");
            }
            _plugins[attribute.PackageName] = new PluginTypeHolder(pluginType, context);
            registered.Add(attribute.PackageName);
        }
        return registered;
    }

    public void RegisterPlugin(string packageName, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType)
    {
        if (_plugins.TryGetValue(packageName, out var existing))
        {
            if (existing.Type == pluginType)
                return;
            throw new InvalidOperationException(
                $"Plugin package '{packageName}' is already registered by {existing.Type.FullName}; cannot also register {pluginType.FullName}.");
        }

        _plugins[packageName] = new PluginTypeHolder(pluginType, null);
    }

    public bool TryCreate(string packageName, out IDshPlugin? plugin)
    {
        if (_plugins.TryGetValue(packageName, out var holder))
        {
            plugin = CreatePlugin(holder.Type, packageName);
            return true;
        }
        plugin = null;
        return false;
    }

    public bool TryCreateDefinition(string packageName, out PluginDefinition? definition)
    {
        if (!_plugins.TryGetValue(packageName, out var holder))
        {
            definition = null;
            return false;
        }
        definition = CreateDefinition(holder.Type, packageName);
        return true;
    }

    public PluginDefinition CreateDefinition(string packageName)
    {
        if (!_plugins.TryGetValue(packageName, out var holder))
        {
            throw new KeyNotFoundException($"Plugin package '{packageName}' is not registered.");
        }
        return CreateDefinition(holder.Type, packageName);
    }

    public AssemblyLoadContext? Remove(string packageName)
    {
        if (!_plugins.Remove(packageName, out var holder))
            return null;
        return holder.Context;
    }

    private static Type FindPluginType(Assembly assembly)
    {
        var types = LoadTypes(assembly);
        var pluginTypes = types
            .Where(type => typeof(IDshPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .ToList();
        if (pluginTypes.Count != 1)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.FullName}' must contain exactly one IDshPlugin implementation to be registered as a plugin, found {pluginTypes.Count}.");
        }
        return pluginTypes[0];
    }

    private static IReadOnlyList<Type> LoadTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToList();
        }
    }

    private static PluginDefinition CreateDefinition([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType, string packageName)
    {
        var instance = CreatePlugin(pluginType, packageName);
        return new PluginDefinition
        {
            Name = packageName,
            Inject = instance.Inject,
            Apply = (ctx, config) => CreatePlugin(pluginType, packageName).Apply(ctx, config),
        };
    }

    private static IDshPlugin CreatePlugin([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType, string packageName)
    {
        if (pluginType.GetConstructor([typeof(string)]) is not null)
            return (IDshPlugin)Activator.CreateInstance(pluginType, packageName)!;
        return (IDshPlugin)Activator.CreateInstance(pluginType)!;
    }

    private sealed class PluginTypeHolder([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type, AssemblyLoadContext? context)
    {
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type Type { get; } = type;

        public AssemblyLoadContext? Context { get; } = context;
    }
}
