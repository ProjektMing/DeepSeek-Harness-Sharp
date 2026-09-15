using System.Diagnostics.CodeAnalysis;
using Dsh.Runtime;

namespace Dsh.Plugins;

/** 插件登记表:每包一条,工厂直接构造实例;形态、能力与入口由描述符承载。 */
public sealed class PluginCatalog
{
    private readonly Dictionary<string, PluginHolder> _plugins = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> PackageNames => _plugins.Keys;

    public IReadOnlyList<PluginDescriptor> Descriptors
        => _plugins.Values.Select(holder => holder.Descriptor).ToList();

    public void Register(PluginDescriptor descriptor, Func<IDshPlugin> create)
        => _plugins[descriptor.Package] = new PluginHolder(descriptor, create);

    public bool TryDescribe(string packageName, [NotNullWhen(true)] out PluginDescriptor? descriptor)
    {
        if (_plugins.TryGetValue(packageName, out var holder))
        {
            descriptor = holder.Descriptor;
            return true;
        }
        descriptor = null;
        return false;
    }

    public bool TryGet(string packageName, [NotNullWhen(true)] out Func<IDshPlugin>? create)
    {
        if (_plugins.TryGetValue(packageName, out var holder))
        {
            create = holder.Create;
            return true;
        }
        create = null;
        return false;
    }

    public PluginDefinition CreateDefinition(string packageName)
    {
        if (!_plugins.TryGetValue(packageName, out var holder))
            throw new KeyNotFoundException($"Plugin package '{packageName}' is not registered.");
        return holder.ToDefinition();
    }

    public bool TryCreateDefinition(string packageName, out PluginDefinition? definition)
    {
        if (!_plugins.TryGetValue(packageName, out var holder))
        {
            definition = null;
            return false;
        }
        definition = holder.ToDefinition();
        return true;
    }

    public void Remove(string packageName) => _plugins.Remove(packageName);

    private sealed record PluginHolder(PluginDescriptor Descriptor, Func<IDshPlugin> Create)
    {
        /** 与旧反射路径同语义:探测实例取 Inject,每次 Apply 构造新实例。 */
        public PluginDefinition ToDefinition()
        {
            var probe = Create();
            return new PluginDefinition
            {
                Name = Descriptor.Package,
                Inject = probe.Inject,
                Apply = (ctx, config) => Create().Apply(ctx, config),
            };
        }
    }
}
