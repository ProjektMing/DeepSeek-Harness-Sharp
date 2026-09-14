using System.Reflection;
using System.Runtime.Loader;

namespace Dsh.Plugins;

/** 可回收插件加载上下文:按插件路径解析依赖,支持 Unload() 后回收。
 *  契约面(Dsh.* 与已加载的框架/共享程序集)统一走默认上下文,避免类型副本导致转换失败。 */
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly HashSet<string> _shared;

    public PluginLoadContext(string pluginPath, string? name = null)
        : base(name ?? $"dsh-plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _shared = new HashSet<string>(
            Default.Assemblies.Select(assembly => assembly.GetName().Name).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || name.StartsWith("Dsh.", StringComparison.Ordinal) || _shared.Contains(name))
            return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
