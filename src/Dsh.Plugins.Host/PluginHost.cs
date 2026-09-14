using System.Reflection;
using System.Runtime.Loader;

namespace Dsh.Plugins;

public sealed class PluginHost
{
    public PluginCatalog Catalog { get; } = new();

    /** 安装目录扫描:只登记插件,启用与否由清单决定。 */
    public void ScanDirectory(string path)
    {
        LoadAssemblies(path);
        RegisterGeneratedCatalog();
    }

    /** 安装目录下 plugins 子目录扫描:登记并返回发现的包名,供调用方默认启用。 */
    public IReadOnlyList<string> ScanPluginDirectory(string path)
    {
        var registered = new List<string>();
        foreach (var assembly in AssembliesIn(path))
            registered.AddRange(Catalog.RegisterAssembly(assembly, replace: true));
        return registered;
    }

    private static IEnumerable<Assembly> AssembliesIn(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*.dll"))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith("Dsh.", StringComparison.Ordinal)
                && !fileName.EndsWith(".Plugin.dll", StringComparison.Ordinal))
            {
                continue;
            }
            var fullPath = Path.GetFullPath(file);
            var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                candidate => string.Equals(candidate.GetName().Name, Path.GetFileNameWithoutExtension(fullPath), StringComparison.OrdinalIgnoreCase));
            if (assembly is null)
            {
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                }
                catch (PlatformNotSupportedException)
                {
                    // NativeAOT 不支持运行时 Assembly.Load*，跳过；插件由生成目录 RegisterGeneratedCatalog 注册。
                    continue;
                }
            }
            yield return assembly;
        }
    }

    private void LoadAssemblies(string path)
    {
        foreach (var assembly in AssembliesIn(path))
            Catalog.RegisterAssembly(assembly);
    }

    public void RegisterGeneratedCatalog()
    {
        foreach (var entry in DshPluginCatalogRegistry.Snapshot())
            Catalog.RegisterPlugin(entry.Package, entry.Implementation);
    }
}
